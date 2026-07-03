// EXPERIMENTAL sender-side downscaler that returns CPU-backed NV12 frames.
//
// Why: on Chromium/Android every HW-encoder submission of a GPU-backed frame
// pays a full-resolution GPU→CPU readback inside the GPU process — once PER
// TIER with the metadata downscaler (each encoder gets a ceiling-coded frame).
// This backend replaces those with one zero-copy `importExternalTexture`, one
// compute pass per tier producing tightly-packed NV12 at TARGET resolution,
// and one explicit small readback per tier. The resulting frames are
// CPU-backed, so Chromium's encoder path skips its internal readback entirely
// (`NdkVideoEncodeAccelerator` ingests mapped planes directly).
//
// Unlike the WebGL cascade, EVERY tier is a new NV12 frame — including the
// ceiling-sized top tier (that full-res readback is the biggest win). The
// normalize stage already treats an unreferenced ceiling as an orphan (the
// metadata backend behaves the same way), so ownership stays correct.
//
// Chromium-gated by the caller: Safari/VideoToolbox prefers GPU-backed frames
// and would regress from a CPU buffer. Selected via
// `video.debug.downscalerMode = 'webgpu'`; Perfetto A/B vs `metadata` decides
// whether this ever becomes a default.

import { getLogs } from 'logging';
import { WebGPUManager } from './manager';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { warnLog } = getLogs('VideoWebGPU');

// Each thread packs 4 horizontal Y bytes into one u32 (plus one interleaved
// UV u32 for even rows), so plane strides only need 4-byte alignment.
// A 2×2 spread of bilinear taps per output pixel keeps ≥2× tiers box-filtered.
const NV12_SHADER = /* wgsl */ `
struct Params {
    dstWidth: u32,
    dstHeight: u32,
    yStrideU32: u32,
    uvOffsetU32: u32,
};
@group(0) @binding(0) var src: texture_external;
@group(0) @binding(1) var samp: sampler;
@group(0) @binding(2) var<storage, read_write> dst: array<u32>;
@group(0) @binding(3) var<uniform> params: Params;

fn sampleBox(uv: vec2f, halfStep: vec2f) -> vec3f {
    return 0.25 * (
          textureSampleBaseClampToEdge(src, samp, uv + vec2f(-halfStep.x, -halfStep.y)).rgb
        + textureSampleBaseClampToEdge(src, samp, uv + vec2f( halfStep.x, -halfStep.y)).rgb
        + textureSampleBaseClampToEdge(src, samp, uv + vec2f(-halfStep.x,  halfStep.y)).rgb
        + textureSampleBaseClampToEdge(src, samp, uv + vec2f( halfStep.x,  halfStep.y)).rgb);
}

// BT.601 limited-range RGB→YCbCr, R'G'B' in 0..1.
fn lumaOf(c: vec3f) -> u32 {
    return u32(clamp(16.0 + dot(c, vec3f(65.481, 128.553, 24.966)), 0.0, 255.0));
}

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) gid: vec3u) {
    let x0 = gid.x * 4u;
    let y = gid.y;
    if (x0 >= params.dstWidth || y >= params.dstHeight) {
        return;
    }
    let texel = vec2f(1.0 / f32(params.dstWidth), 1.0 / f32(params.dstHeight));
    let halfStep = texel * 0.25;
    var yPack = 0u;
    for (var i = 0u; i < 4u; i++) {
        let x = min(x0 + i, params.dstWidth - 1u);
        let uv = vec2f((f32(x) + 0.5) * texel.x, (f32(y) + 0.5) * texel.y);
        yPack |= (lumaOf(sampleBox(uv, halfStep)) & 0xFFu) << (i * 8u);
    }
    dst[y * params.yStrideU32 + gid.x] = yPack;
    if ((y & 1u) == 0u) {
        var uvPack = 0u;
        for (var q = 0u; q < 2u; q++) {
            let cx = min(x0 + q * 2u, params.dstWidth - 2u);
            let uv = vec2f((f32(cx) + 1.0) * texel.x, (f32(y) + 1.0) * texel.y);
            let c = sampleBox(uv, halfStep * 2.0);
            let cb = u32(clamp(128.0 + dot(c, vec3f(-37.797, -74.203, 112.0)), 0.0, 255.0));
            let cr = u32(clamp(128.0 + dot(c, vec3f(112.0, -93.786, -18.214)), 0.0, 255.0));
            uvPack |= (cb & 0xFFu) << (q * 16u);
            uvPack |= (cr & 0xFFu) << (q * 16u + 8u);
        }
        dst[params.uvOffsetU32 + (y >> 1u) * params.yStrideU32 + gid.x] = uvPack;
    }
}
`;

interface TierBuffers {
    byteSize: number;
    storage: GPUBuffer;
    staging: GPUBuffer;
    uniforms: GPUBuffer;
}

const align4 = (v: number): number => (v + 3) & ~3;

export function isWebGpuDownscaleSupported(): boolean {
    return !!(globalThis.navigator as Navigator | undefined)?.gpu;
}

export class WebGpuDownscaler implements DownscalerLike {
    private readonly whenReady: Promise<void>;
    private device: GPUDevice | null = null;
    private pipeline: GPUComputePipeline | null = null;
    private tierBuffers = new Map<string, TierBuffers>();
    private removeLostListener: (() => void) | null = null;
    private disposed = false;

    constructor() {
        if (!isWebGpuDownscaleSupported())
            throw new Error('WebGpuDownscaler: WebGPU not available');
        this.whenReady = this.init();
    }

    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        await this.whenReady;
        const device = this.device;
        const pipeline = this.pipeline;
        if (!device || !pipeline || this.disposed)
            throw new Error('WebGpuDownscaler: device unavailable');

        const src = device.importExternalTexture({ source: input });
        const sampler = WebGPUManager.getSampler();
        const encoder = device.createCommandEncoder();
        const tiers = layers.map(l => this.getTierBuffers(device, l));
        for (let i = 0; i < layers.length; i++) {
            const { width, height } = layers[i];
            const t = tiers[i];
            const yStrideU32 = align4(width) / 4;
            device.queue.writeBuffer(
                t.uniforms, 0,
                new Uint32Array([width, height, yStrideU32, yStrideU32 * height]));
            const bindGroup = device.createBindGroup({
                layout: pipeline.getBindGroupLayout(0),
                entries: [
                    { binding: 0, resource: src },
                    { binding: 1, resource: sampler },
                    { binding: 2, resource: { buffer: t.storage } },
                    { binding: 3, resource: { buffer: t.uniforms } },
                ],
            });
            const pass = encoder.beginComputePass();
            pass.setPipeline(pipeline);
            pass.setBindGroup(0, bindGroup);
            pass.dispatchWorkgroups(Math.ceil(width / 32), Math.ceil(height / 8));
            pass.end();
            encoder.copyBufferToBuffer(t.storage, 0, t.staging, 0, t.byteSize);
        }
        device.queue.submit([encoder.finish()]);

        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        try {
            for (let i = 0; i < layers.length; i++) {
                const { width, height } = layers[i];
                const t = tiers[i];
                await t.staging.mapAsync(GPUMapMode.READ);
                let bytes: Uint8Array;
                try {
                    bytes = new Uint8Array(t.staging.getMappedRange().slice(0));
                } finally {
                    t.staging.unmap();
                }
                const stride = align4(width);
                results[i] = new VideoFrame(bytes, {
                    format: 'NV12',
                    codedWidth: width,
                    codedHeight: height,
                    timestamp: input.timestamp,
                    layout: [
                        { offset: 0, stride },
                        { offset: stride * height, stride },
                    ],
                    colorSpace: {
                        primaries: 'bt709',
                        transfer: 'bt709',
                        matrix: 'smpte170m',
                        fullRange: false,
                    },
                });
            }
            return results as VideoFrame[];
        } catch (e) {
            warnLog?.log('WebGpuDownscaler.process failed:', e);
            for (const r of results) {
                try { r?.close(); } catch { /* ignore */ }
            }
            throw e;
        }
    }

    dispose(): void {
        if (this.disposed)
            return;

        this.disposed = true;
        this.removeLostListener?.();
        this.removeLostListener = null;
        for (const t of this.tierBuffers.values()) {
            try { t.storage.destroy(); } catch { /* ignore */ }
            try { t.staging.destroy(); } catch { /* ignore */ }
            try { t.uniforms.destroy(); } catch { /* ignore */ }
        }
        this.tierBuffers.clear();
        this.device = null;
        this.pipeline = null;
    }

    // Private methods

    private async init(): Promise<void> {
        const device = await WebGPUManager.init();
        this.device = device;
        this.pipeline = device.createComputePipeline({
            layout: 'auto',
            compute: {
                module: device.createShaderModule({ code: NV12_SHADER }),
                entryPoint: 'main',
            },
        });
        // Fail-fast on device loss: the downscale slot recreates us and the
        // fresh instance re-inits (or the factory falls back to metadata).
        this.removeLostListener = WebGPUManager.addLostListener(() => this.dispose());
    }

    private getTierBuffers(device: GPUDevice, layer: LayerSpec): TierBuffers {
        const key = `${layer.width}x${layer.height}`;
        const existing = this.tierBuffers.get(key);
        if (existing)
            return existing;

        const byteSize = align4(layer.width) * layer.height * 3 / 2;
        const created: TierBuffers = {
            byteSize,
            storage: device.createBuffer({
                size: byteSize,
                usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC,
            }),
            staging: device.createBuffer({
                size: byteSize,
                usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST,
            }),
            uniforms: device.createBuffer({
                size: 16,
                usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
            }),
        };
        this.tierBuffers.set(key, created);
        return created;
    }
}

// Sender-side WebGPU downscaler that outputs NV12 VideoFrames at each tier's
// resolution. Motivation: the Android HW encode is dominated by Chromium's
// internal `PrepareCpuFrame → ConvertAndScale` (GPU→CPU readback + libyuv scale
// + RGBA→NV12). The readback is unavoidable from JS, but if we hand the encoder
// a frame that is ALREADY NV12 at exactly the tier size, that libyuv scale +
// convert collapses to ~nothing, and the per-tier readback shrinks from a full
// ceiling RGBA buffer (the `metadata` downscaler keeps coded=ceiling) to a
// target-size NV12 buffer. We do the downscale + RGB→YUV on the GPU and map a
// small buffer instead of running libyuv per tier on the CPU.
//
// Top tier (matching the input's display dims) passes through unchanged (RGBA),
// exactly like the WebGL downscaler — its encoder still reads+converts, but the
// win is on the lower tiers, which today each read back the full ceiling.
//
// If WebGPU is unavailable (or the device is lost), every call delegates to a
// MetadataDownscaler so capture keeps working.

import { getLogs } from 'logging';
import { WebGPUManager } from './manager';
import { MetadataDownscaler } from '../metadata/downscaler';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { infoLog, warnLog } = getLogs('VideoWebGPU');

// Fullscreen quad (triangle-strip, 4 verts). Emits a uv varying in [0,1] with
// the image upright for the external-texture sample (flip if the preview shows
// upside-down).
const FULLSCREEN_VS = /* wgsl */`
struct VOut { @builtin(position) pos: vec4f, @location(0) uv: vec2f };
@vertex fn vs(@builtin(vertex_index) i: u32) -> VOut {
  let x = f32(i & 1u);
  let y = f32((i >> 1u) & 1u);
  var o: VOut;
  o.pos = vec4f(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
  o.uv = vec2f(x, 1.0 - y);
  return o;
}
`;

// BT.709 limited-range RGB(0..1) → Y (R8). textureSampleBaseClampToEdge samples
// the full external frame at the target grid → bilinear downscale.
const Y_FS = /* wgsl */`
@group(0) @binding(0) var src: texture_external;
@group(0) @binding(1) var s: sampler;
@fragment fn fs(@location(0) uv: vec2f) -> @location(0) vec4f {
  let c = textureSampleBaseClampToEdge(src, s, uv).rgb;
  let y = (16.0 + 46.5594*c.r + 156.6294*c.g + 15.8112*c.b) / 255.0;
  return vec4f(y, 0.0, 0.0, 1.0);
}
`;

// BT.709 limited-range RGB → Cb,Cr (RG8, NV12 interleave order = U then V).
const UV_FS = /* wgsl */`
@group(0) @binding(0) var src: texture_external;
@group(0) @binding(1) var s: sampler;
@fragment fn fs(@location(0) uv: vec2f) -> @location(0) vec4f {
  let c = textureSampleBaseClampToEdge(src, s, uv).rgb;
  let cb = (128.0 - 25.6642*c.r - 86.3358*c.g + 112.0*c.b) / 255.0;
  let cr = (128.0 + 112.0*c.r - 101.7303*c.g - 10.2697*c.b) / 255.0;
  return vec4f(cb, cr, 0.0, 1.0);
}
`;

function align256(n: number): number {
    return (n + 255) & ~255;
}

interface TierResources {
    yTex: GPUTexture;
    uvTex: GPUTexture;
    yView: GPUTextureView;
    uvView: GPUTextureView;
    buffer: GPUBuffer;
    yStride: number;
    uvStride: number;
    uvOffset: number;
}

const NV12_COLORSPACE: VideoColorSpaceInit = {
    primaries: 'bt709',
    transfer: 'bt709',
    matrix: 'bt709',
    fullRange: false,
};

export class WebGpuDownscaler implements DownscalerLike {
    private device: GPUDevice | null = null;
    private sampler: GPUSampler | null = null;
    private bgl: GPUBindGroupLayout | null = null;
    private yPipeline: GPURenderPipeline | null = null;
    private uvPipeline: GPURenderPipeline | null = null;
    private initState: 'pending' | 'ready' | 'failed' = 'pending';
    private lostDisposer: (() => void) | null = null;
    private readonly tiers = new Map<string, TierResources>();
    private fallback: MetadataDownscaler | null = null;
    private disposed = false;

    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        if (this.disposed)
            return this.useFallback().process(input, layers);
        if (this.initState === 'pending')
            await this.ensureInit();
        if (this.initState !== 'ready' || !this.device)
            return this.useFallback().process(input, layers);

        try {
            return await this.gpuProcess(input, layers);
        } catch (e) {
            warnLog?.log('WebGpuDownscaler.process failed — falling back to metadata:', e);
            this.markFailed();
            return this.useFallback().process(input, layers);
        }
    }

    private async gpuProcess(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const device = this.device!;
        const topIdx = layers.length - 1;
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        const inW = input.displayWidth || input.codedWidth;
        const inH = input.displayHeight || input.codedHeight;

        const ext = device.importExternalTexture({ source: input });
        const bindGroup = device.createBindGroup({
            layout: this.bgl!,
            entries: [
                { binding: 0, resource: ext },
                { binding: 1, resource: this.sampler! },
            ],
        });
        const encoder = device.createCommandEncoder();
        const pending: { i: number; w: number; h: number; tier: TierResources }[] = [];

        for (let i = topIdx; i >= 0; i--) {
            const { width: w, height: h } = layers[i];
            // Ceiling tier (matches input dims) — pass through RGBA, like WebGL.
            if (w === inW && h === inH) {
                results[i] = input;
                continue;
            }
            // NV12 needs even dims; odd would shear the chroma plane.
            if ((w & 1) !== 0 || (h & 1) !== 0)
                throw new Error(`WebGpuDownscaler: odd tier dims ${w}x${h}`);

            const tier = this.getTier(w, h);
            const yPass = encoder.beginRenderPass({
                colorAttachments: [{ view: tier.yView, loadOp: 'clear', storeOp: 'store', clearValue: { r: 0, g: 0, b: 0, a: 1 } }],
            });
            yPass.setPipeline(this.yPipeline!);
            yPass.setBindGroup(0, bindGroup);
            yPass.draw(4);
            yPass.end();

            const uvPass = encoder.beginRenderPass({
                colorAttachments: [{ view: tier.uvView, loadOp: 'clear', storeOp: 'store', clearValue: { r: 0, g: 0, b: 0, a: 1 } }],
            });
            uvPass.setPipeline(this.uvPipeline!);
            uvPass.setBindGroup(0, bindGroup);
            uvPass.draw(4);
            uvPass.end();

            encoder.copyTextureToBuffer(
                { texture: tier.yTex },
                { buffer: tier.buffer, offset: 0, bytesPerRow: tier.yStride, rowsPerImage: h },
                { width: w, height: h, depthOrArrayLayers: 1 });
            encoder.copyTextureToBuffer(
                { texture: tier.uvTex },
                { buffer: tier.buffer, offset: tier.uvOffset, bytesPerRow: tier.uvStride, rowsPerImage: h / 2 },
                { width: w / 2, height: h / 2, depthOrArrayLayers: 1 });

            pending.push({ i, w, h, tier });
        }

        device.queue.submit([encoder.finish()]);

        // Overlap the per-tier GPU→CPU maps (this IS the readback we trade libyuv
        // for) — issue all copies above, then await all maps together.
        await Promise.all(pending.map(async p => {
            await p.tier.buffer.mapAsync(GPUMapMode.READ);
            const range = p.tier.buffer.getMappedRange();
            // VideoFrame(BufferSource) copies synchronously — safe to unmap after.
            results[p.i] = new VideoFrame(range, {
                format: 'NV12',
                codedWidth: p.w,
                codedHeight: p.h,
                timestamp: input.timestamp,
                colorSpace: NV12_COLORSPACE,
                layout: [
                    { offset: 0, stride: p.tier.yStride },
                    { offset: p.tier.uvOffset, stride: p.tier.uvStride },
                ],
            });
            p.tier.buffer.unmap();
        }));

        return results as VideoFrame[];
    }

    private getTier(w: number, h: number): TierResources {
        const key = `${w}x${h}`;
        const existing = this.tiers.get(key);
        if (existing) return existing;

        const device = this.device!;
        const yStride = align256(w);            // R8: 1 byte/px
        const uvStride = align256(w);           // RG8 half-res row = (w/2)*2 = w bytes
        const uvOffset = yStride * h;           // 256-aligned (yStride is)
        const bufferSize = uvOffset + uvStride * (h / 2);

        const yTex = device.createTexture({
            size: { width: w, height: h },
            format: 'r8unorm',
            usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC,
        });
        const uvTex = device.createTexture({
            size: { width: w / 2, height: h / 2 },
            format: 'rg8unorm',
            usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC,
        });
        const buffer = device.createBuffer({
            size: bufferSize,
            usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
        });
        const tier: TierResources = {
            yTex, uvTex,
            yView: yTex.createView(),
            uvView: uvTex.createView(),
            buffer, yStride, uvStride, uvOffset,
        };
        this.tiers.set(key, tier);
        return tier;
    }

    private async ensureInit(): Promise<void> {
        try {
            const device = await WebGPUManager.init();
            this.device = device;
            this.sampler = WebGPUManager.getSampler();
            this.bgl = device.createBindGroupLayout({
                entries: [
                    { binding: 0, visibility: GPUShaderStage.FRAGMENT, externalTexture: {} },
                    { binding: 1, visibility: GPUShaderStage.FRAGMENT, sampler: {} },
                ],
            });
            const layout = device.createPipelineLayout({ bindGroupLayouts: [this.bgl] });
            const vs = device.createShaderModule({ code: FULLSCREEN_VS });
            this.yPipeline = device.createRenderPipeline({
                layout,
                vertex: { module: vs, entryPoint: 'vs' },
                fragment: { module: device.createShaderModule({ code: Y_FS }), entryPoint: 'fs', targets: [{ format: 'r8unorm' }] },
                primitive: { topology: 'triangle-strip' },
            });
            this.uvPipeline = device.createRenderPipeline({
                layout,
                vertex: { module: vs, entryPoint: 'vs' },
                fragment: { module: device.createShaderModule({ code: UV_FS }), entryPoint: 'fs', targets: [{ format: 'rg8unorm' }] },
                primitive: { topology: 'triangle-strip' },
            });
            this.lostDisposer = WebGPUManager.addLostListener(() => this.markFailed());
            this.initState = 'ready';
            infoLog?.log('WebGpuDownscaler: device + NV12 pipelines ready');
        } catch (e) {
            warnLog?.log('WebGpuDownscaler: init failed — using metadata fallback:', e);
            this.initState = 'failed';
        }
    }

    private markFailed(): void {
        this.initState = 'failed';
        this.yPipeline = null;
        this.uvPipeline = null;
        this.device = null;
    }

    private useFallback(): MetadataDownscaler {
        return (this.fallback ??= new MetadataDownscaler());
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.lostDisposer?.();
        this.lostDisposer = null;
        for (const t of this.tiers.values()) {
            try { t.yTex.destroy(); } catch { /* ignore */ }
            try { t.uvTex.destroy(); } catch { /* ignore */ }
            try { t.buffer.destroy(); } catch { /* ignore */ }
        }
        this.tiers.clear();
        this.fallback?.dispose();
        this.fallback = null;
        this.device = null;
    }
}

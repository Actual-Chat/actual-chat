// Sender-side WebGPU downscaler that outputs NV12 VideoFrames at each tier's
// resolution. Motivation: the Android HW encode is dominated by Chromium's
// internal `PrepareCpuFrame → ConvertAndScale` (GPU→CPU readback + libyuv scale
// + RGBA→NV12). The readback is unavoidable from JS, but if we hand the encoder
// a frame that is ALREADY NV12 at exactly the tier size, that libyuv scale +
// convert collapses (device trace 114921 vs 153125: ConvertAndScale 120→2 ms).
//
// Contention-minimized layout: each lower tier renders Y *and* UV in ONE render
// pass to a single R8 texture sized W × (H + H/2) — the top H rows hold the Y
// plane, the bottom H/2 rows hold the interleaved UV plane — followed by ONE
// copyTextureToBuffer. That single combined target is already laid out as NV12
// (Y rows then UV rows at the same stride), so one map + one VideoFrame finish
// the tier. One pass + one copy per tier (vs two passes + two copies) keeps the
// RenderThread / QueueSubmit load down. All tiers go through one command encoder
// → one queue.submit per frame.
//
// Top tier (matching the input's display dims) passes through unchanged (RGBA),
// like the WebGL downscaler — the win is on the lower tiers. If WebGPU is
// unavailable (or the device is lost), every call delegates to a
// MetadataDownscaler so capture keeps working.

import { getLogs } from 'logging';
import { WebGPUManager } from './manager';
import { MetadataDownscaler } from '../metadata/downscaler';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { infoLog, warnLog } = getLogs('VideoWebGPU');

// One pass → a combined R8 target W×(3H/2). Region split by builtin position:
// rows [0,H) = Y (full-res sample), rows [H,3H/2) = NV12-interleaved UV (even x =
// Cb/U, odd x = Cr/V, chroma sample at half-res). BT.709 limited range. Builtin
// `position` gives target pixel centers; uv = pos/size is upright (matches blur).
const FULLSCREEN_VS = /* wgsl */`
@vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
  let x = f32(i & 1u);
  let y = f32((i >> 1u) & 1u);
  return vec4f(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
}
`;

const NV12_FS = /* wgsl */`
@group(0) @binding(0) var src: texture_external;
@group(0) @binding(1) var s: sampler;
@group(0) @binding(2) var<uniform> dims: vec4f;   // (W, H, _, _)
@fragment fn fs(@builtin(position) pos: vec4f) -> @location(0) vec4f {
  let W = dims.x;
  let H = dims.y;
  let x = pos.x;
  let y = pos.y;
  if (y < H) {
    let c = textureSampleBaseClampToEdge(src, s, vec2f(x / W, y / H)).rgb;
    let yv = (16.0 + 46.5594*c.r + 156.6294*c.g + 15.8112*c.b) / 255.0;
    return vec4f(yv, 0.0, 0.0, 1.0);
  }
  let cy = y - H;                      // [0, H/2)
  let cx = floor(x * 0.5);             // chroma column [0, W/2)
  let su = vec2f((cx + 0.5) / (W * 0.5), (cy + 0.5) / (H * 0.5));
  let c = textureSampleBaseClampToEdge(src, s, su).rgb;
  let cb = (128.0 - 25.6642*c.r - 86.3358*c.g + 112.0*c.b) / 255.0;
  let cr = (128.0 + 112.0*c.r - 101.7303*c.g - 10.2697*c.b) / 255.0;
  let v = select(cb, cr, (u32(x) & 1u) == 1u);
  return vec4f(v, 0.0, 0.0, 1.0);
}
`;

function align256(n: number): number {
    return (n + 255) & ~255;
}

interface TierResources {
    tex: GPUTexture;          // combined NV12-layout R8 target, W × (3H/2)
    view: GPUTextureView;
    dimsBuf: GPUBuffer;       // uniform (W, H)
    buffer: GPUBuffer;        // mappable NV12 readback
    stride: number;          // Y & UV plane stride (256-aligned)
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
    private pipeline: GPURenderPipeline | null = null;
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
        const encoder = device.createCommandEncoder();
        const pending: { i: number; w: number; h: number; tier: TierResources }[] = [];

        for (let i = topIdx; i >= 0; i--) {
            const { width: w, height: h } = layers[i];
            if (w === inW && h === inH) {
                results[i] = input; // ceiling passthrough (RGBA)
                continue;
            }
            if ((w & 1) !== 0 || (h & 1) !== 0)
                throw new Error(`WebGpuDownscaler: odd tier dims ${w}x${h}`);

            const tier = this.getTier(w, h);
            const bindGroup = device.createBindGroup({
                layout: this.bgl!,
                entries: [
                    { binding: 0, resource: ext },
                    { binding: 1, resource: this.sampler! },
                    { binding: 2, resource: { buffer: tier.dimsBuf } },
                ],
            });
            const pass = encoder.beginRenderPass({
                colorAttachments: [{ view: tier.view, loadOp: 'clear', storeOp: 'store', clearValue: { r: 0, g: 0, b: 0, a: 1 } }],
            });
            pass.setPipeline(this.pipeline!);
            pass.setBindGroup(0, bindGroup);
            pass.draw(4);
            pass.end();

            // The combined target is already NV12-laid-out (Y rows then UV rows
            // at the same stride) — one copy covers both planes.
            encoder.copyTextureToBuffer(
                { texture: tier.tex },
                { buffer: tier.buffer, offset: 0, bytesPerRow: tier.stride, rowsPerImage: h + h / 2 },
                { width: w, height: h + h / 2, depthOrArrayLayers: 1 });

            pending.push({ i, w, h, tier });
        }

        device.queue.submit([encoder.finish()]);

        await Promise.all(pending.map(async p => {
            await p.tier.buffer.mapAsync(GPUMapMode.READ);
            const range = p.tier.buffer.getMappedRange();
            results[p.i] = new VideoFrame(range, {
                format: 'NV12',
                codedWidth: p.w,
                codedHeight: p.h,
                timestamp: input.timestamp,
                colorSpace: NV12_COLORSPACE,
                layout: [
                    { offset: 0, stride: p.tier.stride },
                    { offset: p.tier.uvOffset, stride: p.tier.stride },
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
        const combinedH = h + h / 2;
        const stride = align256(w);          // R8: 1 byte/px; Y & UV share this stride
        const uvOffset = stride * h;         // UV plane begins at row H (256-aligned)
        const bufferSize = stride * combinedH;

        const tex = device.createTexture({
            size: { width: w, height: combinedH },
            format: 'r8unorm',
            usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC,
        });
        const dimsBuf = device.createBuffer({
            size: 16,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(dimsBuf, 0, new Float32Array([w, h, 0, 0]));
        const buffer = device.createBuffer({
            size: bufferSize,
            usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
        });
        const tier: TierResources = {
            tex, view: tex.createView(), dimsBuf, buffer, stride, uvOffset,
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
                    { binding: 2, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
                ],
            });
            const layout = device.createPipelineLayout({ bindGroupLayouts: [this.bgl] });
            this.pipeline = device.createRenderPipeline({
                layout,
                vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
                fragment: { module: device.createShaderModule({ code: NV12_FS }), entryPoint: 'fs', targets: [{ format: 'r8unorm' }] },
                primitive: { topology: 'triangle-strip' },
            });
            this.lostDisposer = WebGPUManager.addLostListener(() => this.markFailed());
            this.initState = 'ready';
            infoLog?.log('WebGpuDownscaler: device + single-pass NV12 pipeline ready');
        } catch (e) {
            warnLog?.log('WebGpuDownscaler: init failed — using metadata fallback:', e);
            this.initState = 'failed';
        }
    }

    private markFailed(): void {
        this.initState = 'failed';
        this.pipeline = null;
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
            try { t.tex.destroy(); } catch { /* ignore */ }
            try { t.dimsBuf.destroy(); } catch { /* ignore */ }
            try { t.buffer.destroy(); } catch { /* ignore */ }
        }
        this.tiers.clear();
        this.fallback?.dispose();
        this.fallback = null;
        this.device = null;
    }
}

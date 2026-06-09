// Sender-side WebGPU downscaler that outputs NV12 VideoFrames at each tier's
// resolution. Motivation: the Android HW encode is dominated by Chromium's
// internal `PrepareCpuFrame → ConvertAndScale` (GPU→CPU readback + libyuv scale
// + RGBA→NV12). The readback is unavoidable from JS, but if we hand the encoder
// a frame that is ALREADY NV12 at exactly the tier size, that libyuv scale +
// convert collapses to ~nothing (device trace 114921: ConvertAndScale 120→2 ms).
//
// Two production paths, chosen at init:
//   COMPUTE (option 4, preferred): one compute pass reads the external camera
//     texture with textureLoad (2×2 box-average downscale), computes BT.709
//     limited Y per pixel + Cb/Cr per 2×2, and writes a tightly-packed NV12
//     storage buffer directly. No render passes, no per-tier textures, no
//     copyTextureToBuffer — just dispatches + one copyBufferToBuffer/tier into a
//     shared mappable buffer. This removes the RenderThread cost (the real GPU
//     contention in trace 114921: ~29 s/60s of a core in 2 render passes/tier).
//   RENDER (two-pass fallback): per lower tier, render Y (R8) + UV (RG8) and
//     copyTextureToBuffer into the shared buffer. Kept because external-texture
//     sampling in a compute stage is not universally supported — if the compute
//     pipeline can't be built (or a tier isn't byte-packable) we use this proven
//     path instead of dropping all the way to MetadataDownscaler.
//
// Both paths produce the SAME genuine two-plane NV12 the encoder fast-path needs
// (Y plane then UV plane; a packed single-R8 shape breaks it — see trace 130345).
// Every lower tier's planes land in ONE shared buffer, read back with a SINGLE
// mapAsync per frame. The top tier (== input display dims) passes through RGBA.
//
// If WebGPU itself is unavailable (or the device is lost), delegate to a
// MetadataDownscaler so capture keeps working.

import { getLogs } from 'logging';
import { WebGPUManager } from './manager';
import { MetadataDownscaler } from '../metadata/downscaler';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { warnLog } = getLogs('VideoWebGPU');

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

// Compute RGB→NV12. textureLoad (no sampler) is the compute-safe external-texture
// read; we box-average a 2×2 source footprint to downscale. Y packs 4 pixels per
// u32 word (each invocation owns a word → no read-modify-write races); UV packs 2
// chroma pixels (U,V,U,V) per word. Params via a uniform (two vec4u).
const NV12_COMPUTE = /* wgsl */`
struct Params { a: vec4u, b: vec4u };
@group(0) @binding(0) var src: texture_external;
@group(1) @binding(0) var<storage, read_write> outbuf: array<u32>;
@group(1) @binding(1) var<uniform> P: Params;

fn srcRGB(px: u32, py: u32, dstW: u32, dstH: u32, srcW: u32, srcH: u32) -> vec3f {
  let sx = i32((px * srcW) / dstW);
  let sy = i32((py * srcH) / dstH);
  let mx = i32(srcW) - 1;
  let my = i32(srcH) - 1;
  // Ratio 1 (the top/focused tier, no downscale): sample the exact texel — a 2x2
  // average would soften the full-res stream.
  if (srcW == dstW && srcH == dstH) {
    return textureLoad(src, vec2i(min(sx, mx), min(sy, my))).rgb;
  }
  let a = textureLoad(src, vec2i(min(sx, mx),      min(sy, my))).rgb;
  let b = textureLoad(src, vec2i(min(sx + 1, mx),  min(sy, my))).rgb;
  let c = textureLoad(src, vec2i(min(sx, mx),      min(sy + 1, my))).rgb;
  let d = textureLoad(src, vec2i(min(sx + 1, mx),  min(sy + 1, my))).rgb;
  return (a + b + c + d) * 0.25;
}

@compute @workgroup_size(8, 8)
fn yMain(@builtin(global_invocation_id) gid: vec3u) {
  let dstW = P.a.x; let dstH = P.a.y; let srcW = P.a.z; let srcH = P.a.w;
  let yWordsPerRow = P.b.x;
  let wx = gid.x; let py = gid.y;
  if (wx >= yWordsPerRow || py >= dstH) { return; }
  var word: u32 = 0u;
  for (var k: u32 = 0u; k < 4u; k = k + 1u) {
    let px = wx * 4u + k;
    let rgb = srcRGB(px, py, dstW, dstH, srcW, srcH);
    let yv = clamp(16.0 + 46.5594*rgb.r + 156.6294*rgb.g + 15.8112*rgb.b, 0.0, 255.0);
    word = word | (u32(yv + 0.5) << (k * 8u));
  }
  outbuf[py * yWordsPerRow + wx] = word;
}

@compute @workgroup_size(8, 8)
fn uvMain(@builtin(global_invocation_id) gid: vec3u) {
  let dstW = P.a.x; let dstH = P.a.y; let srcW = P.a.z; let srcH = P.a.w;
  let uvWordBase = P.b.y; let uvWordsPerRow = P.b.z; let dstHHalf = P.b.w;
  let wx = gid.x; let cy = gid.y;
  if (wx >= uvWordsPerRow || cy >= dstHHalf) { return; }
  var word: u32 = 0u;
  for (var j: u32 = 0u; j < 2u; j = j + 1u) {
    let cx = wx * 2u + j;
    let r0 = srcRGB(cx * 2u,      cy * 2u,      dstW, dstH, srcW, srcH);
    let r1 = srcRGB(cx * 2u + 1u, cy * 2u + 1u, dstW, dstH, srcW, srcH);
    let rgb = (r0 + r1) * 0.5;
    let cb = clamp(128.0 - 25.6642*rgb.r - 86.3358*rgb.g + 112.0*rgb.b, 0.0, 255.0);
    let cr = clamp(128.0 + 112.0*rgb.r - 101.7303*rgb.g - 10.2697*rgb.b, 0.0, 255.0);
    word = word | (u32(cb + 0.5) << (j * 16u)) | (u32(cr + 0.5) << (j * 16u + 8u));
  }
  outbuf[uvWordBase + cy * uvWordsPerRow + wx] = word;
}
`;

function align256(n: number): number {
    return (n + 255) & ~255;
}

// Per-tier render targets (render fallback path; cached by W×H).
interface TierTextures {
    yTex: GPUTexture;
    uvTex: GPUTexture;
    yView: GPUTextureView;
    uvView: GPUTextureView;
    stride: number;           // 256-aligned Y & UV plane stride
}

// Per-tier compute resources (cached by W×H): the storage buffer the compute
// pass writes (tight NV12) + its uniform + bind group. srcKey tracks the source
// dims baked into the uniform so we rewrite it if the camera resolution changes.
interface ComputeTier {
    storage: GPUBuffer;
    uniform: GPUBuffer;
    bindGroup: GPUBindGroup;
    size: number;
    srcKey: string;
}

interface Slot {
    yOff: number;
    uvOff: number;
    stride: number;
}

// One mappable buffer holding every tier's [Y plane][UV plane] regions, plus the
// per-tier byte offsets/stride. Rebuilt only when the active tier set changes.
interface SharedReadback {
    buffer: GPUBuffer;
    key: string;
    slots: Map<string, Slot>;
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
    private renderBgl: GPUBindGroupLayout | null = null;
    private yPipeline: GPURenderPipeline | null = null;
    private uvPipeline: GPURenderPipeline | null = null;
    private computeExtBgl: GPUBindGroupLayout | null = null;
    private computeTierBgl: GPUBindGroupLayout | null = null;
    private yCompute: GPUComputePipeline | null = null;
    private uvCompute: GPUComputePipeline | null = null;
    private useCompute = false;
    private readonly forceRender: boolean;
    private initState: 'pending' | 'ready' | 'failed' = 'pending';
    private lostDisposer: (() => void) | null = null;
    private readonly tiers = new Map<string, TierTextures>();
    private readonly computeTiers = new Map<string, ComputeTier>();
    private shared: SharedReadback | null = null;
    private fallback: MetadataDownscaler | null = null;
    private disposed = false;

    constructor(opts?: { forceRender?: boolean }) {
        this.forceRender = opts?.forceRender ?? false;
    }

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
        const topIdx = layers.length - 1;
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        const inW = input.displayWidth || input.codedWidth;
        const inH = input.displayHeight || input.codedHeight;

        // Collect the NV12 (non-ceiling) tiers bottom-first.
        // Every tier — including the top — is produced as NV12. The top tier used
        // to pass through as RGBA, but the encoder then paid a full-res RGBA→NV12
        // ConvertAndScale on it (the dominant remaining cost); producing NV12 at
        // ratio 1 (exact 1:1 sample in the shader) is lossless and skips that.
        const nv12: { i: number; w: number; h: number }[] = [];
        for (let i = topIdx; i >= 0; i--) {
            const { width: w, height: h } = layers[i];
            if ((w & 1) !== 0 || (h & 1) !== 0)
                throw new Error(`WebGpuDownscaler: odd tier dims ${w}x${h}`);
            nv12.push({ i, w, h });
        }
        if (nv12.length === 0)
            return results as VideoFrame[];

        const packable = nv12.every(t => (t.w & 3) === 0 && (t.h & 1) === 0);
        if (this.useCompute && packable)
            this.computePass(input, inW, inH, nv12);
        else
            this.renderPass(input, nv12);

        const shared = this.shared!;
        await shared.buffer.mapAsync(GPUMapMode.READ);
        const range = shared.buffer.getMappedRange();
        for (const t of nv12) {
            const slot = shared.slots.get(`${t.w}x${t.h}`)!;
            // VideoFrame(BufferSource) copies synchronously — safe to unmap after.
            results[t.i] = new VideoFrame(range, {
                format: 'NV12',
                codedWidth: t.w,
                codedHeight: t.h,
                timestamp: input.timestamp,
                colorSpace: NV12_COLORSPACE,
                layout: [
                    { offset: slot.yOff, stride: slot.stride },
                    { offset: slot.uvOff, stride: slot.stride },
                ],
            });
        }
        shared.buffer.unmap();
        return results as VideoFrame[];
    }

    // Option 4: one compute pass writes every tier's NV12 into its storage buffer,
    // then a copyBufferToBuffer per tier collects them into the shared readback.
    private computePass(input: VideoFrame, inW: number, inH: number, nv12: { w: number; h: number }[]): void {
        const device = this.device!;
        const shared = this.ensureShared(nv12, true);
        const ext = device.importExternalTexture({ source: input });
        const extBg = device.createBindGroup({
            layout: this.computeExtBgl!,
            entries: [{ binding: 0, resource: ext }],
        });
        const encoder = device.createCommandEncoder();
        const pass = encoder.beginComputePass();
        for (const t of nv12) {
            const ct = this.getComputeTier(t.w, t.h, inW, inH);
            // Dispatch over the padded word stride so the shader fills full 256-aligned
            // rows (padding maps to clamped edge pixels; the encoder reads only width).
            const wordsW = align256(t.w) / 4;
            pass.setBindGroup(0, extBg);
            pass.setBindGroup(1, ct.bindGroup);
            pass.setPipeline(this.yCompute!);
            pass.dispatchWorkgroups(Math.ceil(wordsW / 8), Math.ceil(t.h / 8));
            pass.setPipeline(this.uvCompute!);
            pass.dispatchWorkgroups(Math.ceil(wordsW / 8), Math.ceil((t.h / 2) / 8));
        }
        pass.end();
        for (const t of nv12) {
            const ct = this.computeTiers.get(`${t.w}x${t.h}`)!;
            const slot = shared.slots.get(`${t.w}x${t.h}`)!;
            encoder.copyBufferToBuffer(ct.storage, 0, shared.buffer, slot.yOff, ct.size);
        }
        device.queue.submit([encoder.finish()]);
    }

    // Render fallback: per tier, render Y (R8) + UV (RG8) and copy both into the
    // shared readback (256-aligned strides for copyTextureToBuffer).
    private renderPass(input: VideoFrame, nv12: { w: number; h: number }[]): void {
        const device = this.device!;
        const shared = this.ensureShared(nv12, false);
        const ext = device.importExternalTexture({ source: input });
        const bindGroup = device.createBindGroup({
            layout: this.renderBgl!,
            entries: [
                { binding: 0, resource: ext },
                { binding: 1, resource: this.sampler! },
            ],
        });
        const encoder = device.createCommandEncoder();
        for (const t of nv12) {
            const tex = this.getTier(t.w, t.h);
            const slot = shared.slots.get(`${t.w}x${t.h}`)!;
            const yPass = encoder.beginRenderPass({
                colorAttachments: [{ view: tex.yView, loadOp: 'clear', storeOp: 'store', clearValue: { r: 0, g: 0, b: 0, a: 1 } }],
            });
            yPass.setPipeline(this.yPipeline!);
            yPass.setBindGroup(0, bindGroup);
            yPass.draw(4);
            yPass.end();

            const uvPass = encoder.beginRenderPass({
                colorAttachments: [{ view: tex.uvView, loadOp: 'clear', storeOp: 'store', clearValue: { r: 0, g: 0, b: 0, a: 1 } }],
            });
            uvPass.setPipeline(this.uvPipeline!);
            uvPass.setBindGroup(0, bindGroup);
            uvPass.draw(4);
            uvPass.end();

            encoder.copyTextureToBuffer(
                { texture: tex.yTex },
                { buffer: shared.buffer, offset: slot.yOff, bytesPerRow: slot.stride, rowsPerImage: t.h },
                { width: t.w, height: t.h, depthOrArrayLayers: 1 });
            encoder.copyTextureToBuffer(
                { texture: tex.uvTex },
                { buffer: shared.buffer, offset: slot.uvOff, bytesPerRow: slot.stride, rowsPerImage: t.h / 2 },
                { width: t.w / 2, height: t.h / 2, depthOrArrayLayers: 1 });
        }
        device.queue.submit([encoder.finish()]);
    }

    // (Re)build the shared readback buffer when the active tier set (or path)
    // changes. Both paths use a 256-aligned plane stride — the HW encoder wants
    // it (a tight stride forces a re-pack). tight (compute) only differs in
    // inter-tier packing: copyBufferToBuffer needs a 4-byte dst offset, while
    // copyTextureToBuffer (render) needs each tier's region 256-aligned.
    private ensureShared(nv12: { w: number; h: number }[], tight: boolean): SharedReadback {
        const device = this.device!;
        const key = (tight ? 'C:' : 'R:') + nv12.map(t => `${t.w}x${t.h}`).join('|');
        if (this.shared?.key === key)
            return this.shared;

        try { this.shared?.buffer.destroy(); } catch { /* ignore */ }
        const slots = new Map<string, Slot>();
        let off = 0;
        for (const t of nv12) {
            const stride = align256(t.w);
            const yOff = off;
            const uvOff = yOff + stride * t.h;
            const end = uvOff + stride * (t.h / 2);
            off = tight ? end : align256(end);
            slots.set(`${t.w}x${t.h}`, { yOff, uvOff, stride });
        }
        const buffer = device.createBuffer({
            size: off,
            usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
        });
        this.shared = { buffer, key, slots };
        return this.shared;
    }

    private getComputeTier(w: number, h: number, inW: number, inH: number): ComputeTier {
        const key = `${w}x${h}`;
        const srcKey = `${inW}x${inH}`;
        const device = this.device!;
        const existing = this.computeTiers.get(key);
        if (existing) {
            if (existing.srcKey !== srcKey) {
                device.queue.writeBuffer(existing.uniform, 0, this.computeParams(w, h, inW, inH));
                existing.srcKey = srcKey;
            }
            return existing;
        }

        const size = align256(w) * h * 3 / 2;
        const storage = device.createBuffer({
            size,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC,
        });
        const uniform = device.createBuffer({
            size: 32,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(uniform, 0, this.computeParams(w, h, inW, inH));
        const bindGroup = device.createBindGroup({
            layout: this.computeTierBgl!,
            entries: [
                { binding: 0, resource: { buffer: storage } },
                { binding: 1, resource: { buffer: uniform } },
            ],
        });
        const tier: ComputeTier = { storage, uniform, bindGroup, size, srcKey };
        this.computeTiers.set(key, tier);
        return tier;
    }

    private computeParams(w: number, h: number, srcW: number, srcH: number): Uint32Array<ArrayBuffer> {
        // Pad each plane row to a 256-byte stride — the HW encoder wants an aligned
        // NV12 input and re-packs a tight one (libyuv ConvertAndScale), so match the
        // render path's align256 geometry. dstW stays w (source mapping unchanged).
        const sw = align256(w);
        const p = new Uint32Array(new ArrayBuffer(32));
        p.set([
            w, h, srcW, srcH,
            sw / 4,         // yWordsPerRow (padded stride, words)
            sw * h / 4,     // uvWordBase (padded Y-plane bytes / 4)
            sw / 4,         // uvWordsPerRow (2 chroma px/word, padded)
            h / 2,          // dstHHalf
        ]);
        return p;
    }

    private getTier(w: number, h: number): TierTextures {
        const key = `${w}x${h}`;
        const existing = this.tiers.get(key);
        if (existing) return existing;

        const device = this.device!;
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
        const tier: TierTextures = {
            yTex, uvTex,
            yView: yTex.createView(),
            uvView: uvTex.createView(),
            stride: align256(w),
        };
        this.tiers.set(key, tier);
        return tier;
    }

    private async ensureInit(): Promise<void> {
        try {
            const device = await WebGPUManager.init();
            this.device = device;
            this.sampler = WebGPUManager.getSampler();
            this.buildRenderPipelines(device);
            this.useCompute = this.forceRender ? false : this.tryBuildComputePipelines(device);
            this.lostDisposer = WebGPUManager.addLostListener(() => this.markFailed());
            this.initState = 'ready';
            warnLog?.log(`WebGpuDownscaler: ready (path=${this.useCompute ? 'compute' : 'render'})`);
        } catch (e) {
            warnLog?.log('WebGpuDownscaler: init failed — using metadata fallback:', e);
            this.initState = 'failed';
        }
    }

    private buildRenderPipelines(device: GPUDevice): void {
        this.renderBgl = device.createBindGroupLayout({
            entries: [
                { binding: 0, visibility: GPUShaderStage.FRAGMENT, externalTexture: {} },
                { binding: 1, visibility: GPUShaderStage.FRAGMENT, sampler: {} },
            ],
        });
        const layout = device.createPipelineLayout({ bindGroupLayouts: [this.renderBgl] });
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
    }

    // external-texture sampling in a compute stage isn't universally supported —
    // if any of this throws, we stay on the proven render path.
    private tryBuildComputePipelines(device: GPUDevice): boolean {
        try {
            this.computeExtBgl = device.createBindGroupLayout({
                entries: [{ binding: 0, visibility: GPUShaderStage.COMPUTE, externalTexture: {} }],
            });
            this.computeTierBgl = device.createBindGroupLayout({
                entries: [
                    { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
                    { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
                ],
            });
            const layout = device.createPipelineLayout({ bindGroupLayouts: [this.computeExtBgl, this.computeTierBgl] });
            const module = device.createShaderModule({ code: NV12_COMPUTE });
            this.yCompute = device.createComputePipeline({ layout, compute: { module, entryPoint: 'yMain' } });
            this.uvCompute = device.createComputePipeline({ layout, compute: { module, entryPoint: 'uvMain' } });
            return true;
        } catch (e) {
            warnLog?.log('WebGpuDownscaler: compute path unavailable — using render path:', e);
            this.yCompute = null;
            this.uvCompute = null;
            return false;
        }
    }

    private markFailed(): void {
        this.initState = 'failed';
        this.yPipeline = null;
        this.uvPipeline = null;
        this.yCompute = null;
        this.uvCompute = null;
        this.useCompute = false;
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
        try { this.shared?.buffer.destroy(); } catch { /* ignore */ }
        this.shared = null;
        for (const t of this.tiers.values()) {
            try { t.yTex.destroy(); } catch { /* ignore */ }
            try { t.uvTex.destroy(); } catch { /* ignore */ }
        }
        this.tiers.clear();
        for (const t of this.computeTiers.values()) {
            try { t.storage.destroy(); } catch { /* ignore */ }
            try { t.uniform.destroy(); } catch { /* ignore */ }
        }
        this.computeTiers.clear();
        this.fallback?.dispose();
        this.fallback = null;
        this.device = null;
    }
}

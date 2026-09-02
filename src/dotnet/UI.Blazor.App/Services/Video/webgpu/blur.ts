// Dual-Kawase pyramid blur on WebGPU. Two public entry points:
//
//   • BgBlurRenderer / applyFullFrameBlur — receiver-side letterbox
//     backdrop. Full-frame, no mask; binds a constant-zero mask texture
//     so the mask-aware shaders behave as a uniform blur.
//
//   • applyBackgroundBlur (+ applyTemporalSmoothing) — camera-blur with
//     a per-pixel person mask supplied from a GPU buffer (e.g. an ONNX
//     segmentation network). Currently has no in-tree caller; kept for
//     future segmentation-based blur work — the pipelines below cost
//     nothing extra when only the full-frame path is used.
//
// Original lived between commits 6cc83df55 (2026-04-30) and 2bbf4c02e
// (2026-05-07). The I420 readback path (`submitBlurI420` +
// `webgpu-yuv-converter.ts`) is intentionally not restored.

import { WebGPUManager } from './manager';
import { BgBlurPerfTracker } from '../services/bg-blur-stats';
import { getLogs } from 'logging';
import { WebCodecsCompat, type FrameSource } from 'web-codecs-compat/init';

const { infoLog, warnLog } = getLogs('VideoWebGPU');

let device: GPUDevice | null = null;
let sampler: GPUSampler;

// Lost-listener disposer. Subscribes in initBlurWebGPU so a GPU device-lost
// nulls all module-scope GPU refs before another consumer dereferences them.
// Unsubscribed only when initBlurWebGPU re-runs against a different device
// (re-arms against the new one) or when the lost handler itself fires
// (handler clears the disposer slot).
let lostDispose: (() => void) | null = null;

let downsample2dPipeline: GPURenderPipeline; // For downsampling texture_2d
let compositePipeline: GPURenderPipeline;
let mipmapDownsamplePipeline: GPURenderPipeline;
let mipmapUpsamplePipeline: GPURenderPipeline;

// Compute pipelines used only by the mask-based applyBackgroundBlur path.
let maskBufferToTexturePipeline: GPUComputePipeline | null = null;
let temporalSmoothingPipeline: GPUComputePipeline | null = null;

// Lazy module-scope OffscreenCanvas used by applyBackgroundBlur (segmentation
// path) as a swap-chain texture target before re-wrapping the result back into
// a VideoFrame. Constructed on first use so simply importing this module is
// safe in non-browser environments (Node-based unit tests) that lack
// OffscreenCanvas. The full-frame BgBlurRenderer does NOT use this — it owns
// its own canvas.
let offscreenCanvas: OffscreenCanvas | null = null;
let canvasCtx: GPUCanvasContext;

function ensureOffscreenCanvas(): OffscreenCanvas {
    offscreenCanvas ??= new OffscreenCanvas(1, 1);
    return offscreenCanvas;
}

let lastBlurStrength = -1;
let cachedLevels = 3;

let FORMAT: GPUTextureFormat = 'bgra8unorm';

// Mipmap texture cache for optimized reuse
const mipmapTextureCache = new Map<string, GPUTexture>();

function getMipmapTexture(w: number, h: number): GPUTexture {
    const key = `${w},${h}`;
    const cached = mipmapTextureCache.get(key);
    if (cached)
        return cached;

    const maxMips = Math.floor(Math.log2(Math.max(w, h))) + 1;
    const texture = device!.createTexture({
        size: { width: w, height: h },
        format: FORMAT,
        usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST,
        mipLevelCount: maxMips,
    });

    mipmapTextureCache.set(key, texture);
    return texture;
}

function clearMipmapCache(): void {
    for (const texture of mipmapTextureCache.values())
        deferTextureDestroy(texture);
    mipmapTextureCache.clear();
    pyramidViewCache.clear();
    cachedPyramidTexture = null;
}

// Cache keys: "width,height,blurStrength" → buffer with correct offset values
const offsetBufferCache = new Map<string, GPUBuffer>();

// 8-byte uniform buffer pool — used by mask upload (dims vec2u) and
// temporal smoothing (params vec2f). Avoids per-frame allocations.
const uniform8BufferPool: GPUBuffer[] = [];

function getUniform8Buffer(): GPUBuffer {
    if (uniform8BufferPool.length > 0)
        return uniform8BufferPool.pop()!;

    return device!.createBuffer({
        size: 8,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });
}

function returnUniform8Buffer(buffer: GPUBuffer): void {
    uniform8BufferPool.push(buffer);
}

const tempUint32Array2 = new Uint32Array(2);
const tempFloat32Array2 = new Float32Array(2);

// Deferred cleanup. Avoids onSubmittedWorkDone() sync points by parking
// destroy() calls a few frames behind the encode rate, after the GPU has
// drained the work that referenced the resource.
let blurFrameCounter = 0;
// Split by what the closure touches. GPU handles die with the device and must not be
// dereferenced afterwards; VideoFrames are unaffected and still have to be closed, so
// a device loss drops the first queue but must drain the second.
const blurCleanupQueue = new Map<number, (() => void)[]>();
const blurFrameCloseQueue = new Map<number, (() => void)[]>();
const BLUR_CLEANUP_DELAY_FRAMES = 3;

function registerDeferred(queue: Map<number, (() => void)[]>, cleanupFn: () => void): void {
    const cleanupFrame = blurFrameCounter + BLUR_CLEANUP_DELAY_FRAMES;
    const cleanups = queue.get(cleanupFrame) ?? [];
    cleanups.push(cleanupFn);
    queue.set(cleanupFrame, cleanups);
}

function registerBlurDeferredCleanup(cleanupFn: () => void): void {
    registerDeferred(blurCleanupQueue, cleanupFn);
}

function deferTextureDestroy(texture: GPUTexture | null): void {
    if (!texture)
        return;

    registerBlurDeferredCleanup(() => texture.destroy());
}

function deferBufferDestroy(buffer: GPUBuffer | null): void {
    if (!buffer)
        return;

    registerBlurDeferredCleanup(() => buffer.destroy());
}

function deferFrameClose(frame: VideoFrame): void {
    registerDeferred(blurFrameCloseQueue, () => {
        try { frame.close(); } catch { /* already closed */ }
    });
}

function clearOffsetBufferCache(): void {
    for (const buf of offsetBufferCache.values())
        deferBufferDestroy(buf);
    offsetBufferCache.clear();
}

export function processBlurDeferredCleanups(currentFrame: number = ++blurFrameCounter): void {
    const minCleanupFrame = currentFrame - BLUR_CLEANUP_DELAY_FRAMES;
    drainDueDeferred(blurCleanupQueue, minCleanupFrame);
    drainDueDeferred(blurFrameCloseQueue, minCleanupFrame);
}

function drainDueDeferred(queue: Map<number, (() => void)[]>, minCleanupFrame: number): void {
    for (const [frameNum, cleanups] of queue) {
        if (frameNum <= minCleanupFrame) {
            runDeferred(cleanups);
            queue.delete(frameNum);
        }
    }
}

function drainAllDeferred(queue: Map<number, (() => void)[]>): void {
    for (const cleanups of queue.values())
        runDeferred(cleanups);
    queue.clear();
}

function runDeferred(cleanups: (() => void)[]): void {
    for (const cleanup of cleanups) {
        try { cleanup(); } catch (e) { warnLog?.log('Error during deferred cleanup:', e); }
    }
}

function getOffsetBuffer(targetW: number, targetH: number, blurStrength: number): GPUBuffer {
    const key = `${targetW},${targetH},${blurStrength}`;
    const cached = offsetBufferCache.get(key);
    if (cached)
        return cached;

    const buffer = device!.createBuffer({
        size: 8,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });
    tempFloat32Array2[0] = blurStrength / targetW;
    tempFloat32Array2[1] = blurStrength / targetH;
    device!.queue.writeBuffer(buffer, 0, tempFloat32Array2);
    offsetBufferCache.set(key, buffer);
    return buffer;
}

// Mask state — populated by uploadMaskFromBuffer (segmentation path).
// The full-frame BgBlurRenderer uses getZeroMaskTexture() instead and
// never touches these.
let cachedMaskTexture: GPUTexture | null = null;
let cachedUpscaledMask: GPUTexture | null = null;
let lastMaskW = 0;
let lastMaskH = 0;

const pyramidViewCache = new Map<number, GPUTextureView>();
let cachedPyramidTexture: GPUTexture | null = null;

function getPyramidView(pyramid: GPUTexture, level: number): GPUTextureView {
    if (cachedPyramidTexture !== pyramid) {
        pyramidViewCache.clear();
        cachedPyramidTexture = pyramid;
    }
    let view = pyramidViewCache.get(level);
    if (!view) {
        view = pyramid.createView({ baseMipLevel: level, mipLevelCount: 1 });
        pyramidViewCache.set(level, view);
    }
    return view;
}

let cachedMaskTextureView: GPUTextureView | null = null;
let cachedMaskTextureForView: GPUTexture | null = null;

function getMaskTextureView(maskTex: GPUTexture): GPUTextureView {
    if (cachedMaskTextureForView !== maskTex) {
        cachedMaskTextureView = maskTex.createView();
        cachedMaskTextureForView = maskTex;
    }
    return cachedMaskTextureView!;
}

const FULLSCREEN_VS = /* wgsl */`
  @vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
    let x = f32(i & 1u);
    let y = f32((i >> 1u) & 1u);
    return vec4f(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
  }
`;

const PERSON_MASK_THRESHOLD = 0.45;

// Downsample from texture_2d (pyramid levels beyond the first).
// Mask path retained so the same pipeline serves the no-mask full-frame
// case: caller binds a 1×1 zero-mask, bgWeight() returns 1 for every
// sample, weights collapse to a uniform Kawase blur.
const DOWNSAMPLE_2D_WGSL = /* wgsl */`
  @group(0) @binding(0) var src: texture_2d<f32>;
  @group(0) @binding(1) var s: sampler;
  @group(0) @binding(2) var<uniform> offset: vec2f;
  @group(0) @binding(3) var mask: texture_2d<f32>;
  @group(0) @binding(4) var maskSampler: sampler;

  const PERSON_THRESHOLD: f32 = ${PERSON_MASK_THRESHOLD};

  fn bgWeight(maskVal: f32) -> f32 {
    return 1.0 - smoothstep(PERSON_THRESHOLD - 0.1, PERSON_THRESHOLD + 0.1, maskVal);
  }

  @fragment fn fs(@builtin(position) pos: vec4f) -> @location(0) vec4f {
    let targetSize = vec2f(textureDimensions(src)) / 2.0;
    let uv = (pos.xy + 0.5) / targetSize;

    let uv0 = uv;
    let uv1 = uv + vec2f(-offset.x, -offset.y);
    let uv2 = uv + vec2f( offset.x, -offset.y);
    let uv3 = uv + vec2f(-offset.x,  offset.y);
    let uv4 = uv + vec2f( offset.x,  offset.y);

    let c0 = textureSample(src, s, uv0);
    let c1 = textureSample(src, s, uv1);
    let c2 = textureSample(src, s, uv2);
    let c3 = textureSample(src, s, uv3);
    let c4 = textureSample(src, s, uv4);

    let m0 = textureSample(mask, maskSampler, uv0).r;
    let m1 = textureSample(mask, maskSampler, uv1).r;
    let m2 = textureSample(mask, maskSampler, uv2).r;
    let m3 = textureSample(mask, maskSampler, uv3).r;
    let m4 = textureSample(mask, maskSampler, uv4).r;

    let w0 = bgWeight(m0) * 4.0;
    let w1 = bgWeight(m1);
    let w2 = bgWeight(m2);
    let w3 = bgWeight(m3);
    let w4 = bgWeight(m4);

    let totalWeight = w0 + w1 + w2 + w3 + w4;
    let weightedColor = c0 * w0 + c1 * w1 + c2 * w2 + c3 * w3 + c4 * w4;

    let hasWeight = totalWeight > 0.01;
    let blurredResult = weightedColor / max(totalWeight, 0.01);

    return select(c0, blurredResult, hasWeight);
  }
`;

// Downsample from VideoFrame (level 1, texture_external source).
const MIPMAP_DOWNSAMPLE_WGSL = /* wgsl */`
 @group(0) @binding(0) var src: texture_external;
 @group(0) @binding(1) var s: sampler;
 @group(0) @binding(2) var<uniform> offset: vec2f;
 @group(0) @binding(3) var mask: texture_2d<f32>;
 @group(0) @binding(4) var maskSampler: sampler;

 const PERSON_THRESHOLD: f32 = ${PERSON_MASK_THRESHOLD};

 fn bgWeight(maskVal: f32) -> f32 {
   return 1.0 - smoothstep(PERSON_THRESHOLD - 0.1, PERSON_THRESHOLD + 0.1, maskVal);
 }

 @fragment fn fs(@builtin(position) pos: vec4f) -> @location(0) vec4f {
   let targetSize = vec2f(textureDimensions(src)) / 2.0;
   let uv = (pos.xy + 0.5) / targetSize;

   let uv0 = uv;
   let uv1 = uv + vec2f(-offset.x, -offset.y);
   let uv2 = uv + vec2f( offset.x, -offset.y);
   let uv3 = uv + vec2f(-offset.x,  offset.y);
   let uv4 = uv + vec2f( offset.x,  offset.y);

   let c0 = textureSampleBaseClampToEdge(src, s, uv0);
   let c1 = textureSampleBaseClampToEdge(src, s, uv1);
   let c2 = textureSampleBaseClampToEdge(src, s, uv2);
   let c3 = textureSampleBaseClampToEdge(src, s, uv3);
   let c4 = textureSampleBaseClampToEdge(src, s, uv4);

   let m0 = textureSample(mask, maskSampler, uv0).r;
   let m1 = textureSample(mask, maskSampler, uv1).r;
   let m2 = textureSample(mask, maskSampler, uv2).r;
   let m3 = textureSample(mask, maskSampler, uv3).r;
   let m4 = textureSample(mask, maskSampler, uv4).r;

   let w0 = bgWeight(m0) * 4.0;
   let w1 = bgWeight(m1);
   let w2 = bgWeight(m2);
   let w3 = bgWeight(m3);
   let w4 = bgWeight(m4);

   let totalWeight = w0 + w1 + w2 + w3 + w4;
   let weightedColor = c0 * w0 + c1 * w1 + c2 * w2 + c3 * w3 + c4 * w4;

   let hasWeight = totalWeight > 0.01;
   let blurredResult = weightedColor / max(totalWeight, 0.01);

   return select(c0, blurredResult, hasWeight);
 }
`;

// Dual-Kawase 9-tap upsample.
const MIPMAP_UPSCALE_WGSL = /* wgsl */`
 @group(0) @binding(0) var src: texture_2d<f32>;
 @group(0) @binding(1) var s: sampler;
 @group(0) @binding(2) var<uniform> offset: vec2f;
 @group(0) @binding(3) var mask: texture_2d<f32>;
 @group(0) @binding(4) var maskSampler: sampler;

 const PERSON_THRESHOLD: f32 = ${PERSON_MASK_THRESHOLD};

 fn bgWeight(maskVal: f32) -> f32 {
   return 1.0 - smoothstep(PERSON_THRESHOLD - 0.1, PERSON_THRESHOLD + 0.1, maskVal);
 }

 @fragment fn fs(@builtin(position) pos: vec4f) -> @location(0) vec4f {
   let targetSize = vec2f(textureDimensions(src)) * 2.0;
   let uv = (pos.xy + 0.5) / targetSize;

   let uv_d0 = uv + vec2f(-offset.x, -offset.y);
   let uv_d1 = uv + vec2f( offset.x, -offset.y);
   let uv_d2 = uv + vec2f(-offset.x,  offset.y);
   let uv_d3 = uv + vec2f( offset.x,  offset.y);
   let ax = offset.x * 2.0;
   let ay = offset.y * 2.0;
   let uv_a0 = uv + vec2f(-ax, 0.0);
   let uv_a1 = uv + vec2f( ax, 0.0);
   let uv_a2 = uv + vec2f(0.0, -ay);
   let uv_a3 = uv + vec2f(0.0,  ay);

   let c0  = textureSample(src, s, uv);
   let cd0 = textureSample(src, s, uv_d0);
   let cd1 = textureSample(src, s, uv_d1);
   let cd2 = textureSample(src, s, uv_d2);
   let cd3 = textureSample(src, s, uv_d3);
   let ca0 = textureSample(src, s, uv_a0);
   let ca1 = textureSample(src, s, uv_a1);
   let ca2 = textureSample(src, s, uv_a2);
   let ca3 = textureSample(src, s, uv_a3);

   let m0  = textureSample(mask, maskSampler, uv).r;
   let md0 = textureSample(mask, maskSampler, uv_d0).r;
   let md1 = textureSample(mask, maskSampler, uv_d1).r;
   let md2 = textureSample(mask, maskSampler, uv_d2).r;
   let md3 = textureSample(mask, maskSampler, uv_d3).r;
   let ma0 = textureSample(mask, maskSampler, uv_a0).r;
   let ma1 = textureSample(mask, maskSampler, uv_a1).r;
   let ma2 = textureSample(mask, maskSampler, uv_a2).r;
   let ma3 = textureSample(mask, maskSampler, uv_a3).r;

   let w0  = bgWeight(m0) * 4.0;
   let wd0 = bgWeight(md0) * 2.0;
   let wd1 = bgWeight(md1) * 2.0;
   let wd2 = bgWeight(md2) * 2.0;
   let wd3 = bgWeight(md3) * 2.0;
   let wa0 = bgWeight(ma0);
   let wa1 = bgWeight(ma1);
   let wa2 = bgWeight(ma2);
   let wa3 = bgWeight(ma3);

   let totalWeight = w0 + wd0 + wd1 + wd2 + wd3 + wa0 + wa1 + wa2 + wa3;
   let weightedColor = c0 * w0
       + cd0 * wd0 + cd1 * wd1 + cd2 * wd2 + cd3 * wd3
       + ca0 * wa0 + ca1 * wa1 + ca2 * wa2 + ca3 * wa3;

   let hasWeight = totalWeight > 0.01;
   let blurredResult = weightedColor / max(totalWeight, 0.01);

   return select(c0, blurredResult, hasWeight);
 }
`;

// EMA blend: smoothedMask[i] = mix(smoothedMask[i], currentMask[i], alpha).
// Used by the segmentation path to dampen per-frame mask jitter.
const TEMPORAL_SMOOTHING_WGSL = /* wgsl */`
  @group(0) @binding(0) var<storage, read> currentMask: array<f32>;
  @group(0) @binding(1) var<storage, read_write> smoothedMask: array<f32>;
  @group(0) @binding(2) var<uniform> params: vec2f; // alpha, element count

  @compute @workgroup_size(256)
  fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let count = u32(params.y);
    if (id.x >= count) { return; }

    let alpha = params.x;
    let current = currentMask[id.x];
    let previous = smoothedMask[id.x];
    smoothedMask[id.x] = mix(previous, current, alpha);
  }
`;

// Float32 mask GPUBuffer → rgba8unorm texture (R channel carries mask value).
// Lets segmentation networks deliver masks as GPU-side buffers without a
// CPU roundtrip.
const MASK_BUFFER_TO_TEXTURE_WGSL = /* wgsl */`
  @group(0) @binding(0) var<storage, read> srcBuffer: array<f32>;
  @group(0) @binding(1) var dstTexture: texture_storage_2d<rgba8unorm, write>;
  @group(0) @binding(2) var<uniform> dims: vec2u; // width, height

 @compute @workgroup_size(16, 16)
 fn main(@builtin(global_invocation_id) id: vec3<u32>) {
   let width = dims.x;
   let height = dims.y;

   if (id.x >= width || id.y >= height) { return; }

   let index = id.y * width + id.x;
   let value = srcBuffer[index];
   textureStore(dstTexture, id.xy, vec4f(value, 0.0, 0.0, 1.0));
 }
`;

const COMPOSITE_WGSL = /* wgsl */`
  @group(0) @binding(0) var original: texture_external;
  @group(0) @binding(1) var blurred:   texture_2d<f32>;
  @group(0) @binding(2) var mask:      texture_2d<f32>;
  @group(0) @binding(3) var blurSampler: sampler;
  @group(0) @binding(4) var<uniform> outSize: vec2f;

  @fragment fn fs(@builtin(position) pos: vec4f) -> @location(0) vec4f {
      let uv = pos.xy / outSize;

      let orig = textureSampleBaseClampToEdge(original, blurSampler, uv);
      let blur = textureSample(blurred, blurSampler, uv);

      let maskValue = textureSample(mask, blurSampler, uv).r;
      let alpha = smoothstep(0.30, 0.60, maskValue);

      return mix(blur, orig, alpha);
  }
`;

export async function initBlurWebGPU(gpuDevice?: GPUDevice): Promise<void> {
    const sharedDevice = await WebGPUManager.init(gpuDevice);

    if (device) {
        if (device === sharedDevice)
            return;

        infoLog?.log('Reinitializing blur with new shared device');
    }

    lostDispose?.();
    device = sharedDevice;
    lostDispose = WebGPUManager.addLostListener(onBlurDeviceLost);

    initializeGpuResources();
}

// Drop every module-scope GPU ref so the next call after device.lost re-arms
// cleanly against a fresh device. Cannot .destroy() the cached
// textures/buffers — handles are already dead and dereffing them re-triggers
// the same Dawn "external Instance reference no longer exists" path that
// crashes the renderer. Maps are .clear()ed, and so is the GPU cleanup queue (its
// closures hold .destroy() calls against dead handles) — but the frame-close queue
// is drained instead: those VideoFrames outlive the device and back-pressure the
// decoder until they are closed.
function onBlurDeviceLost(): void {
    warnLog?.log('Blur invalidated by device.lost');
    device = null;
    sampler = null!;
    canvasCtx = null!;
    downsample2dPipeline = null!;
    compositePipeline = null!;
    mipmapDownsamplePipeline = null!;
    mipmapUpsamplePipeline = null!;
    maskBufferToTexturePipeline = null;
    temporalSmoothingPipeline = null;
    mipmapTextureCache.clear();
    pyramidViewCache.clear();
    cachedPyramidTexture = null;
    offsetBufferCache.clear();
    uniform8BufferPool.length = 0;
    cachedMaskTexture = null;
    cachedUpscaledMask = null;
    cachedMaskTextureView = null;
    cachedMaskTextureForView = null;
    lastMaskW = 0;
    lastMaskH = 0;
    zeroMaskTexture = null;
    cachedOutSizeBuffer = null;
    cachedOutSizeKey = '';
    lastBlurStrength = -1;
    blurCleanupQueue.clear();
    drainAllDeferred(blurFrameCloseQueue);
    lostDispose = null;
}

function ensureInitialized(): void {
    if (!device)
        throw new Error('WebGPU blur not initialized. Call initBlurWebGPU() first.');
}

function initializeGpuResources(): void {
    if (!device)
        throw new Error('Device not initialized');

    try {
        const gpu = navigator.gpu as GPU | undefined;
        if (gpu && typeof gpu.getPreferredCanvasFormat === 'function')
            FORMAT = gpu.getPreferredCanvasFormat();
        else
            FORMAT = 'bgra8unorm';
    } catch {
        FORMAT = 'bgra8unorm';
    }

    sampler = WebGPUManager.getSampler();

    canvasCtx = ensureOffscreenCanvas().getContext('webgpu')!;
    canvasCtx.configure({
        device,
        format: FORMAT,
        alphaMode: 'premultiplied',
        usage: GPUTextureUsage.RENDER_ATTACHMENT,
    });

    const blurLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.FRAGMENT, externalTexture: {} },
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, sampler: {} },
            { binding: 2, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
            { binding: 3, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            { binding: 4, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
        ],
    });

    const upsampleLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, sampler: {} },
            { binding: 2, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
            { binding: 3, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            { binding: 4, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
        ],
    });

    const compositeLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.FRAGMENT, externalTexture: {} },
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            { binding: 2, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            { binding: 3, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
            { binding: 4, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
        ],
    });

    downsample2dPipeline = device.createRenderPipeline({
        layout: device.createPipelineLayout({ bindGroupLayouts: [upsampleLayout] }),
        vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
        fragment: { module: device.createShaderModule({ code: DOWNSAMPLE_2D_WGSL }), entryPoint: 'fs', targets: [{ format: FORMAT }] },
        primitive: { topology: 'triangle-strip' },
    });

    compositePipeline = device.createRenderPipeline({
        layout: device.createPipelineLayout({ bindGroupLayouts: [compositeLayout] }),
        vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
        fragment: { module: device.createShaderModule({ code: COMPOSITE_WGSL }), entryPoint: 'fs', targets: [{ format: FORMAT }] },
        primitive: { topology: 'triangle-strip' },
    });

    mipmapDownsamplePipeline = device.createRenderPipeline({
        layout: device.createPipelineLayout({ bindGroupLayouts: [blurLayout] }),
        vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
        fragment: { module: device.createShaderModule({ code: MIPMAP_DOWNSAMPLE_WGSL }), entryPoint: 'fs', targets: [{ format: FORMAT }] },
        primitive: { topology: 'triangle-strip' },
    });

    mipmapUpsamplePipeline = device.createRenderPipeline({
        layout: device.createPipelineLayout({ bindGroupLayouts: [upsampleLayout] }),
        vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
        fragment: { module: device.createShaderModule({ code: MIPMAP_UPSCALE_WGSL }), entryPoint: 'fs', targets: [{ format: FORMAT }] },
        primitive: { topology: 'triangle-strip' },
    });

    maskBufferToTexturePipeline = device.createComputePipeline({
        layout: 'auto',
        compute: {
            module: device.createShaderModule({ code: MASK_BUFFER_TO_TEXTURE_WGSL }),
            entryPoint: 'main',
        },
    });

    temporalSmoothingPipeline = device.createComputePipeline({
        layout: 'auto',
        compute: {
            module: device.createShaderModule({ code: TEMPORAL_SMOOTHING_WGSL }),
            entryPoint: 'main',
        },
    });
}

let cachedOutSizeBuffer: GPUBuffer | null = null;
let cachedOutSizeKey = '';

function encodePyramidAndComposite(
    encoder: GPUCommandEncoder,
    frame: VideoFrame,
    maskTex: GPUTexture,
    blurStrength: number,
    frameW: number,
    frameH: number,
    outW: number,
    outH: number,
    renderTargetView: GPUTextureView,
): void {
    // WebGPU spec: importExternalTexture creates a texture bound to THIS
    // frame's GPU resource and is implicitly destroyed when the frame is
    // closed or the browser advances. NEVER cache across frames.
    const src = device!.importExternalTexture({ source: frame });

    const offsetMultiplier = 0.5;
    const levels = cachedLevels;
    const pyramid = getMipmapTexture(frameW, frameH);

    // Downsample: render to mip 1, then mip 2, etc.
    let currentSrc: GPUExternalTexture | GPUTextureView = src;
    for (let level = 1; level < levels; level++) {
        const offset = getOffsetBuffer(frameW >> level, frameH >> level, blurStrength * offsetMultiplier);
        const isFirstLevel = level === 1;
        const pipeline = isFirstLevel ? mipmapDownsamplePipeline : downsample2dPipeline;

        const levelView = getPyramidView(pyramid, level);
        const mView = getMaskTextureView(maskTex);

        const pass = encoder.beginRenderPass({
            colorAttachments: [{ view: levelView, loadOp: 'clear', storeOp: 'store' }],
        });
        pass.setPipeline(pipeline);
        pass.setBindGroup(0, device!.createBindGroup({
            layout: pipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: currentSrc },
                { binding: 1, resource: sampler },
                { binding: 2, resource: { buffer: offset } },
                { binding: 3, resource: mView },
                { binding: 4, resource: sampler },
            ],
        }));
        pass.draw(4);
        pass.end();

        currentSrc = levelView;
    }

    // Upsample: from deepest mip back to mip 0.
    const maskView = getMaskTextureView(maskTex);
    for (let level = levels - 1; level > 0; level--) {
        const srcView = getPyramidView(pyramid, level);
        const targetView = getPyramidView(pyramid, level - 1);
        const offset = getOffsetBuffer(frameW >> (level - 1), frameH >> (level - 1), blurStrength * offsetMultiplier);

        const pass = encoder.beginRenderPass({
            colorAttachments: [{ view: targetView, loadOp: 'load', storeOp: 'store' }],
        });
        pass.setPipeline(mipmapUpsamplePipeline);
        pass.setBindGroup(0, device!.createBindGroup({
            layout: mipmapUpsamplePipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: srcView },
                { binding: 1, resource: sampler },
                { binding: 2, resource: { buffer: offset } },
                { binding: 3, resource: maskView },
                { binding: 4, resource: sampler },
            ],
        }));
        pass.draw(4);
        pass.end();
    }

    const sizeKey = `${outW},${outH}`;
    if (cachedOutSizeKey !== sizeKey || !cachedOutSizeBuffer) {
        cachedOutSizeBuffer ??= device!.createBuffer({
            size: 8,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        tempFloat32Array2[0] = outW;
        tempFloat32Array2[1] = outH;
        device!.queue.writeBuffer(cachedOutSizeBuffer, 0, tempFloat32Array2);
        cachedOutSizeKey = sizeKey;
    }

    const compositePass = encoder.beginRenderPass({
        colorAttachments: [{ view: renderTargetView, loadOp: 'clear', storeOp: 'store' }],
    });
    compositePass.setPipeline(compositePipeline);
    compositePass.setBindGroup(0, device!.createBindGroup({
        layout: compositePipeline.getBindGroupLayout(0),
        entries: [
            { binding: 0, resource: src },
            { binding: 1, resource: getPyramidView(pyramid, 0) },
            { binding: 2, resource: getMaskTextureView(maskTex) },
            { binding: 3, resource: sampler },
            { binding: 4, resource: { buffer: cachedOutSizeBuffer } },
        ],
    }));
    compositePass.draw(4);
    compositePass.end();
}

// 1×1 mask filled with 0.0 → bgWeight() returns 1 for every sample →
// existing mask-aware shaders behave as a uniform full-frame blur. Lets
// the no-mask BgBlurRenderer reuse the same pipelines that the segmentation
// path used, no shader-level branching required.
let zeroMaskTexture: GPUTexture | null = null;

function getZeroMaskTexture(): GPUTexture {
    if (zeroMaskTexture)
        return zeroMaskTexture;

    zeroMaskTexture = device!.createTexture({
        size: { width: 1, height: 1 },
        format: 'rgba8unorm',
        usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST,
    });
    device!.queue.writeTexture(
        { texture: zeroMaskTexture },
        new Uint8Array([0, 0, 0, 255]),
        { bytesPerRow: 4 },
        { width: 1, height: 1 },
    );
    return zeroMaskTexture;
}

// ============================================================
// Mask-based segmentation blur (camera-blur path, no in-tree caller yet)
// ============================================================

// Output data type for mask buffers.
export type MaskDataType = 'float32';

export interface BlurOptions {
    // Blur strength in pixels (default: 12)
    blurStrength?: number;
    // Whether the mask buffer has new data since last call (default: true).
    // When false, skips the GPU mask upload and reuses the cached mask texture.
    maskDirty?: boolean;
    // GPU buffer containing the raw (unsmoothed) mask for temporal smoothing.
    // When provided together with smoothingAlpha, temporal smoothing is merged
    // into the same GPU command encoder as the blur — saving one queue.submit().
    smoothingSource?: GPUBuffer;
    // Smoothing factor for temporal EMA (0–1). Required when smoothingSource is set.
    smoothingAlpha?: number;
    // Target output width (default: input frame width)
    outputWidth?: number;
    // Target output height (default: input frame height)
    outputHeight?: number;
}

// Upload a float32 mask GPUBuffer to an rgba8unorm texture using the
// compute shader above. Caches the texture across frames when dims match.
function uploadMaskFromBuffer(
    encoder: GPUCommandEncoder,
    maskBuffer: GPUBuffer,
    w: number,
    h: number,
): GPUTexture {
    if (cachedUpscaledMask) {
        deferTextureDestroy(cachedUpscaledMask);
        cachedUpscaledMask = null;
    }

    if (!cachedMaskTexture || lastMaskW !== w || lastMaskH !== h) {
        deferTextureDestroy(cachedMaskTexture);

        cachedMaskTexture = device!.createTexture({
            size: { width: w, height: h },
            format: 'rgba8unorm',
            usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.STORAGE_BINDING,
        });
        cachedMaskTextureView = null;
        cachedMaskTextureForView = null;
        lastMaskW = w;
        lastMaskH = h;
    }

    const dimsBuffer = getUniform8Buffer();
    tempUint32Array2[0] = w;
    tempUint32Array2[1] = h;
    device!.queue.writeBuffer(dimsBuffer, 0, tempUint32Array2);

    const pipeline = maskBufferToTexturePipeline!;
    const computePass = encoder.beginComputePass();
    computePass.setPipeline(pipeline);
    computePass.setBindGroup(0, device!.createBindGroup({
        layout: pipeline.getBindGroupLayout(0),
        entries: [
            { binding: 0, resource: { buffer: maskBuffer } },
            { binding: 1, resource: getMaskTextureView(cachedMaskTexture) },
            { binding: 2, resource: { buffer: dimsBuffer } },
        ],
    }));
    computePass.dispatchWorkgroups(Math.ceil(w / 16), Math.ceil(h / 16));
    computePass.end();

    registerBlurDeferredCleanup(() => returnUniform8Buffer(dimsBuffer));

    return cachedMaskTexture;
}

// Encode temporal smoothing onto an existing command encoder. Does NOT
// submit — caller submits along with the rest of the blur work.
function encodeTemporalSmoothing(
    encoder: GPUCommandEncoder,
    currentMaskBuffer: GPUBuffer,
    smoothedMaskBuffer: GPUBuffer,
    elementCount: number,
    alpha: number,
): void {
    const paramsBuffer = getUniform8Buffer();
    tempFloat32Array2[0] = alpha;
    tempFloat32Array2[1] = elementCount;
    device!.queue.writeBuffer(paramsBuffer, 0, tempFloat32Array2);

    const computePass = encoder.beginComputePass();
    computePass.setPipeline(temporalSmoothingPipeline!);
    computePass.setBindGroup(0, device!.createBindGroup({
        layout: temporalSmoothingPipeline!.getBindGroupLayout(0),
        entries: [
            { binding: 0, resource: { buffer: currentMaskBuffer } },
            { binding: 1, resource: { buffer: smoothedMaskBuffer } },
            { binding: 2, resource: { buffer: paramsBuffer } },
        ],
    }));
    computePass.dispatchWorkgroups(Math.ceil(elementCount / 256));
    computePass.end();

    registerBlurDeferredCleanup(() => returnUniform8Buffer(paramsBuffer));
}

// Standalone GPU submission of temporal smoothing. Use only when NOT using
// the merged path via BlurOptions.smoothingSource.
export function applyTemporalSmoothing(
    currentMaskBuffer: GPUBuffer,
    smoothedMaskBuffer: GPUBuffer,
    elementCount: number,
    alpha: number,
): void {
    ensureInitialized();

    const encoder = device!.createCommandEncoder();
    encodeTemporalSmoothing(encoder, currentMaskBuffer, smoothedMaskBuffer, elementCount, alpha);
    device!.queue.submit([encoder.finish()]);
}

// Encode the full mask-based blur sequence onto an existing command encoder:
//   1. (optional) temporal smoothing of the mask buffer
//   2. mask upload (buffer → texture) when dirty or cache miss
//   3. dual-Kawase pyramid + composite
function encodeBlurPasses(
    encoder: GPUCommandEncoder,
    frame: VideoFrame,
    personMask: GPUBuffer,
    maskWidth: number,
    maskHeight: number,
    opts: {
        blurStrength: number;
        maskDirty: boolean;
        smoothingSource?: GPUBuffer;
        smoothingAlpha?: number;
    },
    frameW: number,
    frameH: number,
    outW: number,
    outH: number,
    renderTargetView: GPUTextureView,
): void {
    const { blurStrength, maskDirty, smoothingSource, smoothingAlpha } = opts;

    if (smoothingSource && smoothingAlpha !== undefined && maskDirty) {
        const maskSize = maskWidth * maskHeight;
        encodeTemporalSmoothing(encoder, smoothingSource, personMask, maskSize, smoothingAlpha);
    }

    let maskTex: GPUTexture;
    if (maskDirty || !cachedMaskTexture || lastMaskW !== maskWidth || lastMaskH !== maskHeight)
        maskTex = uploadMaskFromBuffer(encoder, personMask, maskWidth, maskHeight);
    else
        maskTex = cachedMaskTexture;

    encodePyramidAndComposite(encoder, frame, maskTex, blurStrength, frameW, frameH, outW, outH, renderTargetView);
}

// Apply mask-based background blur. `personMask` is a GPU float32 buffer
// (the per-pixel person-probability map from a segmentation network).
// Returns a new VideoFrame containing the blurred result; the input frame
// is closed via the deferred cleanup queue.
export function applyBackgroundBlur(
    frame: VideoFrame,
    personMask: GPUBuffer,
    maskWidth: number,
    maskHeight: number,
    blurStrengthOrOptions: number | BlurOptions = 12,
): VideoFrame {
    ensureInitialized();
    processBlurDeferredCleanups();

    let blurStrength = 12;
    let maskDirty = true;
    let smoothingSource: GPUBuffer | undefined;
    let smoothingAlpha: number | undefined;
    let outputWidth: number | undefined;
    let outputHeight: number | undefined;

    if (typeof blurStrengthOrOptions === 'number') {
        blurStrength = blurStrengthOrOptions;
    } else {
        blurStrength = blurStrengthOrOptions.blurStrength ?? 12;
        maskDirty = blurStrengthOrOptions.maskDirty ?? true;
        smoothingSource = blurStrengthOrOptions.smoothingSource;
        smoothingAlpha = blurStrengthOrOptions.smoothingAlpha;
        outputWidth = blurStrengthOrOptions.outputWidth;
        outputHeight = blurStrengthOrOptions.outputHeight;
    }

    if (blurStrength !== lastBlurStrength) {
        clearMipmapCache();
        clearOffsetBufferCache();
        cachedLevels = blurStrength < 10 ? 2 : blurStrength < 20 ? 3 : 4;
        lastBlurStrength = blurStrength;
    }

    const w = frame.displayWidth;
    const h = frame.displayHeight;
    if (w === 0 || h === 0)
        throw new Error('Invalid frame');

    const outW = outputWidth ?? w;
    const outH = outputHeight ?? h;

    const canvasEl = ensureOffscreenCanvas();
    if (canvasEl.width !== outW || canvasEl.height !== outH) {
        canvasEl.width = outW;
        canvasEl.height = outH;
    }

    const encoder = device!.createCommandEncoder();
    encodeBlurPasses(
        encoder, frame, personMask, maskWidth, maskHeight,
        { blurStrength, maskDirty, smoothingSource, smoothingAlpha },
        w, h, outW, outH, canvasCtx.getCurrentTexture().createView());

    device!.queue.submit([encoder.finish()]);

    // No GPU sync needed: WebGPU guarantees command ordering within the
    // same queue, and new VideoFrame(canvasEl) implicitly waits
    // for render completion.
    const timestamp = frame.timestamp;
    deferFrameClose(frame);

    return new VideoFrame(canvasEl, { timestamp });
}

// ============================================================
// Full-frame, no-mask blur (receiver-side backdrop)
// ============================================================

// Render a full-frame Kawase blur of `frame` into `targetCtx`'s current
// canvas texture. Caller owns `frame` and must close it after this returns;
// this function does NOT close the frame.
export function applyFullFrameBlur(
    frame: VideoFrame,
    targetCtx: GPUCanvasContext,
    blurStrength = 4,
): void {
    ensureInitialized();
    processBlurDeferredCleanups();

    if (blurStrength !== lastBlurStrength) {
        clearMipmapCache();
        clearOffsetBufferCache();
        cachedLevels = blurStrength < 10 ? 2 : blurStrength < 20 ? 3 : 4;
        lastBlurStrength = blurStrength;
    }

    const w = frame.displayWidth;
    const h = frame.displayHeight;
    if (w === 0 || h === 0)
        return;

    const target = targetCtx.getCurrentTexture();
    const outW = target.width;
    const outH = target.height;

    const source = frame.clone();
    try {
        const encoder = device!.createCommandEncoder();
        encodePyramidAndComposite(
            encoder, source, getZeroMaskTexture(), blurStrength,
            w, h, outW, outH, target.createView());
        device!.queue.submit([encoder.finish()]);
        deferFrameClose(source);
    } catch (e) {
        source.close();
        throw e;
    }
}

// Owns a target OffscreenCanvas + its GPUCanvasContext for full-frame blur.
// Self-initializes on first `render()` (fire-and-forget — first call may
// no-op while WebGPU init is in flight). Used by the player worker to paint
// the focused tile's letterbox backdrop without a main-thread `<video>` pump.
export class BgBlurRenderer {
    private readonly canvas: OffscreenCanvas;
    private ctx: GPUCanvasContext | null = null;
    private initStarted = false;
    private initFailed = false;
    // Subscribed in the constructor; cleared by dispose() or by the lost
    // handler itself when it fires. Without this, after a GPU device-lost
    // the cached `ctx` keeps a dead device reference; next render() would
    // submit work to a dead Dawn handle and crash the renderer.
    private lostDispose: (() => void) | null = null;
    private readonly perf = new BgBlurPerfTracker('webgpu');

    constructor(canvas: OffscreenCanvas) {
        this.canvas = canvas;
        this.lostDispose = WebGPUManager.addLostListener(() => {
            warnLog?.log('BgBlurRenderer invalidated by device.lost');
            // Drop the dead ctx; do NOT call ctx.unconfigure() — that
            // dereferences the dead device. Letting GC reclaim it is fine
            // because the canvas itself is JS-owned.
            this.ctx = null;
            // Allow ensureInit to re-run once a new device is up. initFailed
            // stays false because device.lost is recoverable, not permanent.
            this.initStarted = false;
            this.lostDispose = null;
        });
    }

    // Releases the lost-listener subscription. Call this when the hosting
    // consumer is disposed (e.g. the receiving peer's player worker is
    // replaced) so the listener Set in WebGPUManager doesn't leak.
    dispose(): void {
        this.lostDispose?.();
        this.lostDispose = null;
        this.ctx = null;
        this.initStarted = false;
    }

    // Returns true if the blur ran, false if WebGPU isn't ready yet (or
    // initialization failed permanently). Caller does NOT lose ownership of
    // `frame`.
    render(frame: FrameSource, blurStrength = 4): boolean {
        if (this.initFailed)
            return false;
        // WebGPU's importExternalTexture takes a native VideoFrame and nothing else,
        // so a polyfilled realm (which hands the tap an ImageBitmap) has no path
        // here. The realm test comes first because at level `full` the VideoFrame
        // global IS the polyfill class, so instanceof would wave a plain object
        // through. The controller just leaves the backdrop unpainted, as before init.
        if (WebCodecsCompat.isPolyfilledRealm
            || typeof VideoFrame === 'undefined'
            || !(frame instanceof VideoFrame))
            return false;

        if (!this.ctx) {
            this.ensureInit();
            return false;
        }

        try {
            const t0 = performance.now();
            applyFullFrameBlur(frame, this.ctx, blurStrength);
            this.perf.sample(performance.now() - t0);
            return true;
        } catch (e) {
            warnLog?.log('BgBlurRenderer render failed:', e);
            return false;
        }
    }

    private ensureInit(): void {
        if (this.initStarted)
            return;

        this.initStarted = true;
        void (async () => {
            try {
                await initBlurWebGPU();
                const ctx = this.canvas.getContext('webgpu');
                if (!ctx)
                    throw new Error('webgpu canvas context unavailable');

                ctx.configure({
                    device: WebGPUManager.get(),
                    format: FORMAT,
                    alphaMode: 'opaque',
                    usage: GPUTextureUsage.RENDER_ATTACHMENT,
                });
                this.ctx = ctx;
            } catch (e) {
                this.initFailed = true;
                warnLog?.log('BgBlurRenderer init failed:', e);
            }
        })();
    }
}

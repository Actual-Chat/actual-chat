import { WebGPUManager } from './webgpu-manager.js';
import {
    initYUVConverter,
    getOrCreateCompositeTexture,
    getCompositeTextureView,
    encodeRGBAtoI420,
    ensureStagingReady,
    startReadbackWithCallback,
} from './webgpu-yuv-converter.js';
import { getLogs } from 'logging';

const { infoLog, warnLog } = getLogs('VideoSegmentation');

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


const offscreenCanvas = new OffscreenCanvas(1, 1);
let canvasCtx: GPUCanvasContext;

let lastBlurStrength = -1;
let cachedLevels = 3;

// Texture format - will be set dynamically from navigator.gpu.getPreferredCanvasFormat()
let FORMAT: GPUTextureFormat = 'bgra8unorm'; // Default fallback

// Mipmap texture cache for optimized reuse
const mipmapTextureCache = new Map<string, GPUTexture>();

// Get or create mipmap texture with proper mip level support
const getMipmapTexture = (w: number, h: number): GPUTexture => {
    const key = `${w},${h}`;
    const cached = mipmapTextureCache.get(key);
    if (cached) return cached;

    const maxMips = Math.floor(Math.log2(Math.max(w, h))) + 1;
    const texture = device!.createTexture({
        size: { width: w, height: h },
        format: FORMAT,
        usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST,
        mipLevelCount: maxMips
    });

    mipmapTextureCache.set(key, texture);
    return texture;
};

// Clear mipmap cache when blur strength changes
const clearMipmapCache = () => {
    for (const texture of mipmapTextureCache.values()) {
        deferTextureDestroy(texture);
    }
    mipmapTextureCache.clear();
    pyramidViewCache.clear();
    cachedPyramidTexture = null;
};

// Cache keys: "width,height,blurStrength" → buffer with correct offset values
const offsetBufferCache = new Map<string, GPUBuffer>();

// Uniform buffer pool for 8-byte buffers (vec2f or vec2u)
// Reduces per-frame allocation overhead for small uniform buffers
const uniform8BufferPool: GPUBuffer[] = [];

/**
 * Get a pooled 8-byte uniform buffer
 * Used for dimension buffers, size buffers, etc.
 */
function getUniform8Buffer(): GPUBuffer {
    if (uniform8BufferPool.length > 0) {
        return uniform8BufferPool.pop()!;
    }
    return device!.createBuffer({
        size: 8,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
    });
}

/**
 * Return an 8-byte uniform buffer to the pool
 */
function returnUniform8Buffer(buffer: GPUBuffer): void {
    uniform8BufferPool.push(buffer);
}

// Pre-allocated TypedArrays to avoid per-frame allocations
const tempUint32Array2 = new Uint32Array(2);
const tempFloat32Array2 = new Float32Array(2);


// Deferred cleanup system to eliminate sync points
let blurFrameCounter = 0;
const blurCleanupQueue = new Map<number, (() => void)[]>();

// Frames to wait before cleaning up resources (balances memory usage vs sync overhead)
const BLUR_CLEANUP_DELAY_FRAMES = 3;

/**
  * Register a cleanup function to be executed after GPU work is done
  * Eliminates the need for onSubmittedWorkDone() sync points
  */
function registerBlurDeferredCleanup(cleanupFn: () => void): void {
    const cleanupFrame = blurFrameCounter + BLUR_CLEANUP_DELAY_FRAMES;
    const cleanups = blurCleanupQueue.get(cleanupFrame) ?? [];
    cleanups.push(cleanupFn);
    blurCleanupQueue.set(cleanupFrame, cleanups);
}

function deferTextureDestroy(texture: GPUTexture | null): void {
    if (!texture) return;
    registerBlurDeferredCleanup(() => texture.destroy());
}

function deferBufferDestroy(buffer: GPUBuffer | null): void {
    if (!buffer) return;
    registerBlurDeferredCleanup(() => buffer.destroy());
}

function deferFrameClose(frame: VideoFrame): void {
    registerBlurDeferredCleanup(() => {
        try { frame.close(); } catch { /* already closed */ }
    });
}

function clearOffsetBufferCache(): void {
    for (const buf of offsetBufferCache.values())
        deferBufferDestroy(buf);
    offsetBufferCache.clear();
}

/**
  * Process cleanups for completed frames
  * Call this periodically to clean up resources without blocking
  */
export function processBlurDeferredCleanups(currentFrame: number = ++blurFrameCounter): void {
    // Clean up frames that are sufficiently old
    const minCleanupFrame = currentFrame - BLUR_CLEANUP_DELAY_FRAMES;
    for (const [frameNum, cleanups] of blurCleanupQueue) {
        if (frameNum <= minCleanupFrame) {
            for (const cleanup of cleanups) {
                try {
                    cleanup();
                } catch (error) {
                    warnLog?.log('Error during deferred cleanup:', error);
                }
            }
            blurCleanupQueue.delete(frameNum);
        }
    }
}

function getOffsetBuffer(targetW: number, targetH: number, blurStrength: number): GPUBuffer {
    const key = `${targetW},${targetH},${blurStrength}`;

    // Fast path: exact match (most common after first few frames)
    if (offsetBufferCache.has(key)) {
        return offsetBufferCache.get(key)!;
    }

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

// Mask cache
let cachedMaskTexture: GPUTexture | null = null;
let cachedUpscaledMask: GPUTexture | null = null;
let lastMaskW = 0;
let lastMaskH = 0;

// Texture view caches — avoid 15+ createView() calls per frame
// Pyramid views: keyed by mip level, invalidated when pyramid texture changes
const pyramidViewCache = new Map<number, GPUTextureView>();
let cachedPyramidTexture: GPUTexture | null = null;

// Mask texture view: single view, invalidated when mask texture changes
let cachedMaskTextureView: GPUTextureView | null = null;
let cachedMaskTextureForView: GPUTexture | null = null;

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

function getMaskTextureView(maskTex: GPUTexture): GPUTextureView {
    if (cachedMaskTextureForView !== maskTex) {
        cachedMaskTextureView = maskTex.createView();
        cachedMaskTextureForView = maskTex;
    }
    return cachedMaskTextureView!;
}

// Pipeline for copying mask from GPUBuffer to texture (float32 input)
let maskBufferToTexturePipeline: GPUComputePipeline | null = null;

// Pipeline for temporal mask smoothing (EMA blend)
let temporalSmoothingPipeline: GPUComputePipeline | null = null;


// Fixed fullscreen vertex shader (WGSL strict mode compliant)
const FULLSCREEN_VS = /* wgsl */`
  @vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
    let x = f32(i & 1u);
    let y = f32((i >> 1u) & 1u);
    return vec4f(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
  }
`;

// Mask threshold for person detection during blur (unified with composite and config)
const PERSON_MASK_THRESHOLD = 0.45;

// Downsample from texture_2d (for pyramid levels beyond first) - mask-aware
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

    // Sample positions
    let uv0 = uv;
    let uv1 = uv + vec2f(-offset.x, -offset.y);
    let uv2 = uv + vec2f( offset.x, -offset.y);
    let uv3 = uv + vec2f(-offset.x,  offset.y);
    let uv4 = uv + vec2f( offset.x,  offset.y);

    // Sample ALL colors unconditionally
    let c0 = textureSample(src, s, uv0);
    let c1 = textureSample(src, s, uv1);
    let c2 = textureSample(src, s, uv2);
    let c3 = textureSample(src, s, uv3);
    let c4 = textureSample(src, s, uv4);

    // Sample ALL mask values unconditionally
    let m0 = textureSample(mask, maskSampler, uv0).r;
    let m1 = textureSample(mask, maskSampler, uv1).r;
    let m2 = textureSample(mask, maskSampler, uv2).r;
    let m3 = textureSample(mask, maskSampler, uv3).r;
    let m4 = textureSample(mask, maskSampler, uv4).r;

    // Convert to background weights
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

// Mipmap optimization for mobile devices
// Mobile-specific mipmap-based blur that reduces GPU load
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

   // Sample positions
   let uv0 = uv;
   let uv1 = uv + vec2f(-offset.x, -offset.y);
   let uv2 = uv + vec2f( offset.x, -offset.y);
   let uv3 = uv + vec2f(-offset.x,  offset.y);
   let uv4 = uv + vec2f( offset.x,  offset.y);

   // Sample ALL colors unconditionally (required for uniform control flow)
   let c0 = textureSampleBaseClampToEdge(src, s, uv0);
   let c1 = textureSampleBaseClampToEdge(src, s, uv1);
   let c2 = textureSampleBaseClampToEdge(src, s, uv2);
   let c3 = textureSampleBaseClampToEdge(src, s, uv3);
   let c4 = textureSampleBaseClampToEdge(src, s, uv4);

   // Sample ALL mask values unconditionally
   let m0 = textureSample(mask, maskSampler, uv0).r;
   let m1 = textureSample(mask, maskSampler, uv1).r;
   let m2 = textureSample(mask, maskSampler, uv2).r;
   let m3 = textureSample(mask, maskSampler, uv3).r;
   let m4 = textureSample(mask, maskSampler, uv4).r;

   // Convert to background weights (0 = person, 1 = background)
   let w0 = bgWeight(m0) * 4.0; // center has weight 4 in Dual Kawase
   let w1 = bgWeight(m1);
   let w2 = bgWeight(m2);
   let w3 = bgWeight(m3);
   let w4 = bgWeight(m4);

   // Weighted sum
   let totalWeight = w0 + w1 + w2 + w3 + w4;
   let weightedColor = c0 * w0 + c1 * w1 + c2 * w2 + c3 * w3 + c4 * w4;

   // If almost no background samples, return center color (person area)
   // Use select instead of if to maintain uniform control flow
   let hasWeight = totalWeight > 0.01;
   let blurredResult = weightedColor / max(totalWeight, 0.01);

   return select(c0, blurredResult, hasWeight);
 }
`;

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

   // Dual Kawase upsample: 4 diagonal (weight 2) + 4 axis-aligned (weight 1) + center (weight 4)
   // Diagonal half-pixel offsets
   let uv_d0 = uv + vec2f(-offset.x, -offset.y);
   let uv_d1 = uv + vec2f( offset.x, -offset.y);
   let uv_d2 = uv + vec2f(-offset.x,  offset.y);
   let uv_d3 = uv + vec2f( offset.x,  offset.y);
   // Axis-aligned full-pixel offsets (2x distance)
   let ax = offset.x * 2.0;
   let ay = offset.y * 2.0;
   let uv_a0 = uv + vec2f(-ax, 0.0);
   let uv_a1 = uv + vec2f( ax, 0.0);
   let uv_a2 = uv + vec2f(0.0, -ay);
   let uv_a3 = uv + vec2f(0.0,  ay);

   // Sample ALL colors unconditionally (uniform control flow)
   let c0  = textureSample(src, s, uv);
   let cd0 = textureSample(src, s, uv_d0);
   let cd1 = textureSample(src, s, uv_d1);
   let cd2 = textureSample(src, s, uv_d2);
   let cd3 = textureSample(src, s, uv_d3);
   let ca0 = textureSample(src, s, uv_a0);
   let ca1 = textureSample(src, s, uv_a1);
   let ca2 = textureSample(src, s, uv_a2);
   let ca3 = textureSample(src, s, uv_a3);

   // Sample ALL mask values unconditionally
   let m0  = textureSample(mask, maskSampler, uv).r;
   let md0 = textureSample(mask, maskSampler, uv_d0).r;
   let md1 = textureSample(mask, maskSampler, uv_d1).r;
   let md2 = textureSample(mask, maskSampler, uv_d2).r;
   let md3 = textureSample(mask, maskSampler, uv_d3).r;
   let ma0 = textureSample(mask, maskSampler, uv_a0).r;
   let ma1 = textureSample(mask, maskSampler, uv_a1).r;
   let ma2 = textureSample(mask, maskSampler, uv_a2).r;
   let ma3 = textureSample(mask, maskSampler, uv_a3).r;

   // Convert to background weights: center ×4, diagonal ×2, axis-aligned ×1
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

// Composite shader - blends original video with blurred version using mask
// Note: pos.xy already contains pixel-center coordinates (0.5, 0.5 for first pixel)
const COMPOSITE_WGSL = /* wgsl */`
  @group(0) @binding(0) var original: texture_external;
  @group(0) @binding(1) var blurred:   texture_2d<f32>;
  @group(0) @binding(2) var mask:      texture_2d<f32>;
  @group(0) @binding(3) var blurSampler: sampler;
  @group(0) @binding(4) var<uniform> outSize: vec2f;

  @fragment fn fs(@builtin(position) pos: vec4f) -> @location(0) vec4f {
      // Scale: map output pixel position to [0,1] UV covering the full source frame
      let uv = pos.xy / outSize;

      let orig = textureSampleBaseClampToEdge(original, blurSampler, uv);
      let blur = textureSample(blurred, blurSampler, uv);

      // The mask is already upscaled and smoothed, sample directly with linear filtering
      let maskValue = textureSample(mask, blurSampler, uv).r;

      // Apply smoothstep for final edge refinement (symmetric around 0.45)
      let alpha = smoothstep(0.30, 0.60, maskValue);

      // Alpha is now 0.0-1.0 probability, use directly for blending
      // Higher probability = more person (less blur)
      return mix(blur, orig, alpha);
  }
`;

// Temporal mask smoothing compute shader - blends current mask with previous via EMA
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

// Compute shader to copy mask from GPUBuffer to rgba8unorm texture
// This allows us to use GPU buffer-backed masks directly without CPU roundtrip
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

/**
 * Initialize WebGPU blur with an external device (shared with ONNX Runtime)
 * Must be called BEFORE using applyBackgroundBlur
 */
export async function initBlurWebGPU(gpuDevice?: GPUDevice): Promise<void> {
    const sharedDevice = await WebGPUManager.init(gpuDevice);

    if (device) {
        if (device === sharedDevice) {
            return;
        }
        infoLog?.log('Reinitializing blur with new shared device');
    }

    // Drop prior subscription before re-arming. Either the previous device
    // is the live one we just compared against (and we're switching), or
    // the previous handler already fired and cleared the slot (no-op).
    lostDispose?.();
    device = sharedDevice;
    lostDispose = WebGPUManager.addLostListener(onBlurDeviceLost);

    // Initialize GPU resources with the provided device
    initializeGpuResources();

    // Initialize YUV converter for I420 output
    initYUVConverter(device);
}

// Drop every module-scope GPU ref so the next call after device.lost re-arms
// cleanly against a fresh device. Cannot .destroy() the cached
// textures/buffers — handles are already dead and dereffing them re-triggers
// the same Dawn "external Instance reference no longer exists" path that
// crashes the renderer. Maps are .clear()ed; pools are length-zeroed.
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
    lastBlurStrength = -1;
    // Drop deferred cleanups — their closures hold .destroy() calls against
    // dead GPU handles. Running them after lost would re-trigger the crash.
    blurCleanupQueue.clear();
    lostDispose = null;
}

// Ensure device is initialized (must call initBlurWebGPU first)
function ensureInitialized() {
    if (!device) {
        throw new Error('WebGPU blur not initialized. Call initBlurWebGPU() first.');
    }
}

// Separated GPU resource initialization (can be called with external device)
function initializeGpuResources() {
    if (!device) throw new Error('Device not initialized');

    // Query preferred canvas format for maximum compatibility
    // This returns 'bgra8unorm' on desktop Chrome/Safari, 'rgba8unorm' on some mobile
    try {
        const gpu = navigator.gpu as GPU | undefined;
        if (gpu && typeof gpu.getPreferredCanvasFormat === 'function') {
            FORMAT = gpu.getPreferredCanvasFormat();
        } else {
            FORMAT = 'bgra8unorm'; // Fallback
        }
    } catch {
        FORMAT = 'bgra8unorm'; // Fallback for workers without navigator.gpu
    }

    sampler = WebGPUManager.getSampler();

    canvasCtx = offscreenCanvas.getContext('webgpu')!;
    canvasCtx.configure({
        device,
        format: FORMAT,
        alphaMode: 'premultiplied',
        usage: GPUTextureUsage.RENDER_ATTACHMENT
    });

    // Layout for downsample from VideoFrame (external texture) with mask
    const blurLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.FRAGMENT, externalTexture: {} },
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, sampler: {} },
            { binding: 2, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
            { binding: 3, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } }, // upscaled mask (bgra8unorm)
            { binding: 4, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } }
        ]
    });

    // Layout for downsample/upsample from texture_2d with mask
    const upsampleLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, sampler: {} },
            { binding: 2, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
            { binding: 3, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } }, // upscaled mask (bgra8unorm)
            { binding: 4, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } }
        ]
    });

    const compositeLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.FRAGMENT, externalTexture: {} },
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            { binding: 2, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } }, // upscaled mask (now bgra8unorm, filterable)
            { binding: 3, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
            { binding: 4, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } }, // outSize vec2f
        ]
    });

    downsample2dPipeline = device.createRenderPipeline({
        layout: device.createPipelineLayout({ bindGroupLayouts: [upsampleLayout] }),
        vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
        fragment: { module: device.createShaderModule({ code: DOWNSAMPLE_2D_WGSL }), entryPoint: 'fs', targets: [{ format: FORMAT }] },
        primitive: { topology: 'triangle-strip' }
    });

    compositePipeline = device.createRenderPipeline({
        layout: device.createPipelineLayout({ bindGroupLayouts: [compositeLayout] }),
        vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
        fragment: { module: device.createShaderModule({ code: COMPOSITE_WGSL }), entryPoint: 'fs', targets: [{ format: FORMAT }] },
        primitive: { topology: 'triangle-strip' }
    });

    mipmapDownsamplePipeline = device.createRenderPipeline({
        layout: device.createPipelineLayout({ bindGroupLayouts: [blurLayout] }),
        vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
        fragment: { module: device.createShaderModule({ code: MIPMAP_DOWNSAMPLE_WGSL }), entryPoint: 'fs', targets: [{ format: FORMAT }] },
        primitive: { topology: 'triangle-strip' }
    });

    mipmapUpsamplePipeline = device.createRenderPipeline({
        layout: device.createPipelineLayout({ bindGroupLayouts: [upsampleLayout] }),
        vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
        fragment: { module: device.createShaderModule({ code: MIPMAP_UPSCALE_WGSL }), entryPoint: 'fs', targets: [{ format: FORMAT }] },
        primitive: { topology: 'triangle-strip' }
    });



    const maskBufferToTextureModule = device.createShaderModule({ code: MASK_BUFFER_TO_TEXTURE_WGSL });
    maskBufferToTexturePipeline = device.createComputePipeline({
        layout: 'auto',
        compute: { module: maskBufferToTextureModule, entryPoint: 'main' }
    });

    const temporalSmoothingModule = device.createShaderModule({ code: TEMPORAL_SMOOTHING_WGSL });
    temporalSmoothingPipeline = device.createComputePipeline({
        layout: 'auto',
        compute: { module: temporalSmoothingModule, entryPoint: 'main' }
    });
}


// Upload mask from GPUBuffer directly (no CPU roundtrip)
// Returns the encoder that needs to be submitted before using the texture
function uploadMaskFromBuffer(
    encoder: GPUCommandEncoder,
    maskBuffer: GPUBuffer,
    w: number,
    h: number
): GPUTexture {
    // Always invalidate upscaled mask cache when using buffer path
    // (We can't easily hash the GPU buffer content)
    if (cachedUpscaledMask) {
        deferTextureDestroy(cachedUpscaledMask);
        cachedUpscaledMask = null;
    }

    // Create or reuse mask texture if dimensions match
    if (!cachedMaskTexture || lastMaskW !== w || lastMaskH !== h) {
        deferTextureDestroy(cachedMaskTexture);

        cachedMaskTexture = device!.createTexture({
            size: { width: w, height: h },
            format: 'rgba8unorm', // filterable mask format
            usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.STORAGE_BINDING
        });
        cachedMaskTextureView = null;
        cachedMaskTextureForView = null;
        lastMaskW = w;
        lastMaskH = h;
    }

    // Get pooled uniform buffer for dimensions (reduces allocation overhead)
    const dimsBuffer = getUniform8Buffer();
    tempUint32Array2[0] = w;
    tempUint32Array2[1] = h;
  device!.queue.writeBuffer(dimsBuffer, 0, tempUint32Array2);

  // Use the float32 pipeline
  const pipeline = maskBufferToTexturePipeline!;

  // Run compute shader to copy from buffer to texture
  const computePass = encoder.beginComputePass();
  computePass.setPipeline(pipeline);
  computePass.setBindGroup(0, device!.createBindGroup({
      layout: pipeline.getBindGroupLayout(0),
      entries: [
          { binding: 0, resource: { buffer: maskBuffer } },
          { binding: 1, resource: getMaskTextureView(cachedMaskTexture) },
          { binding: 2, resource: { buffer: dimsBuffer } }
      ]
  }));

  const workgroupsX = Math.ceil(w / 16);
  const workgroupsY = Math.ceil(h / 16);
  computePass.dispatchWorkgroups(workgroupsX, workgroupsY);
  computePass.end();

  // Schedule deferred cleanup (no sync point)
  registerBlurDeferredCleanup(() => {
      returnUniform8Buffer(dimsBuffer);
  });

  return cachedMaskTexture;
}

/**
 * Output data type for mask buffers
 */
export type MaskDataType = 'float32';

/**
 * Options for background blur processing
 */
export interface BlurOptions {
  /** Blur strength in pixels (default: 12) */
  blurStrength?: number;
  /** Whether the mask buffer has new data since last call (default: true).
   *  When false, skips the GPU mask upload and reuses the cached mask texture. */
  maskDirty?: boolean;
  /** GPU buffer containing the raw (unsmoothed) mask for temporal smoothing.
   *  When provided together with smoothingAlpha, temporal smoothing is merged
   *  into the same GPU command encoder as the blur — saving one queue.submit(). */
  smoothingSource?: GPUBuffer;
  /** Smoothing factor for temporal EMA (0-1). Required when smoothingSource is set. */
  smoothingAlpha?: number;
  /** Target output width (default: input frame width) */
  outputWidth?: number;
  /** Target output height (default: input frame height) */
  outputHeight?: number;
}

/**
 * Apply temporal smoothing to a mask buffer using exponential moving average.
 * Blends the current mask with the smoothed (previous) mask in-place on the GPU.
 * @param currentMaskBuffer GPU buffer containing the current frame's raw mask
 * @param smoothedMaskBuffer GPU buffer containing the smoothed mask (read+written in-place)
 * @param elementCount Number of float32 elements in the mask (width * height)
 * @param alpha Smoothing factor (0-1). Lower = more smoothing.
 */
/**
 * Encode temporal smoothing compute pass onto an existing command encoder.
 * Does NOT submit — caller is responsible for submitting.
 */
function encodeTemporalSmoothing(
    encoder: GPUCommandEncoder,
    currentMaskBuffer: GPUBuffer,
    smoothedMaskBuffer: GPUBuffer,
    elementCount: number,
    alpha: number
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
            { binding: 2, resource: { buffer: paramsBuffer } }
        ]
    }));
    computePass.dispatchWorkgroups(Math.ceil(elementCount / 256));
    computePass.end();

    registerBlurDeferredCleanup(() => {
        returnUniform8Buffer(paramsBuffer);
    });
}

/**
 * Apply temporal smoothing as a standalone GPU submission.
 * Use this only when NOT using the merged path via BlurOptions.smoothingSource.
 */
export function applyTemporalSmoothing(
    currentMaskBuffer: GPUBuffer,
    smoothedMaskBuffer: GPUBuffer,
    elementCount: number,
    alpha: number
): void {
    ensureInitialized();

    const encoder = device!.createCommandEncoder();
    encodeTemporalSmoothing(encoder, currentMaskBuffer, smoothedMaskBuffer, elementCount, alpha);
    device!.queue.submit([encoder.finish()]);
}

// Cached crop offset buffer — reused when dimensions don't change
let cachedOutSizeBuffer: GPUBuffer | null = null;
let cachedOutSizeKey = '';

// Public API - accepts either CPU Float32Array or GPU buffer
export function applyBackgroundBlur(
    frame: VideoFrame,
    personMask: GPUBuffer,
    maskWidth: number,
    maskHeight: number,
    blurStrengthOrOptions: number | BlurOptions = 12
): VideoFrame {
    ensureInitialized();
    processBlurDeferredCleanups();

    // Parse options
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

    // Clear caches on blur strength change
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

    // Output dimensions: use specified output size or default to frame size
    const outW = outputWidth ?? w;
    const outH = outputHeight ?? h;

    // Set offscreen canvas to output dimensions (not frame dimensions)
    if (offscreenCanvas.width !== outW || offscreenCanvas.height !== outH) {
        offscreenCanvas.width = outW;
        offscreenCanvas.height = outH;
    }

    const encoder = device!.createCommandEncoder();

    encodeBlurPasses(encoder, frame, personMask, maskWidth, maskHeight, {
        blurStrength,
        maskDirty,
        smoothingSource,
        smoothingAlpha,
    }, w, h, outW, outH, canvasCtx.getCurrentTexture().createView());

    device!.queue.submit([encoder.finish()]);

    // No GPU sync needed: WebGPU guarantees command ordering within the same queue,
    // and new VideoFrame(offscreenCanvas) implicitly waits for render completion.
    const timestamp = frame.timestamp;
    deferFrameClose(frame);

    return new VideoFrame(offscreenCanvas, { timestamp });
}

/**
 * Internal helper: encode all blur passes (temporal smoothing → mask upload →
 * pyramid downsample/upsample → composite) onto an existing command encoder.
 * The composite is rendered to `renderTargetView`, which can be the canvas
 * texture or a separate composite texture for I420 conversion.
 */
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

    // Merge temporal smoothing into this encoder (saves one queue.submit per frame)
    if (smoothingSource && smoothingAlpha !== undefined && maskDirty) {
        const maskSize = maskWidth * maskHeight;
        encodeTemporalSmoothing(encoder, smoothingSource, personMask, maskSize, smoothingAlpha);
    }

    // Upload mask from GPU buffer (skip if mask hasn't changed and cache is valid)
    let maskTex: GPUTexture;
    if (maskDirty || !cachedMaskTexture || lastMaskW !== maskWidth || lastMaskH !== maskHeight) {
        maskTex = uploadMaskFromBuffer(encoder, personMask, maskWidth, maskHeight);
    } else {
        maskTex = cachedMaskTexture;
    }

    encodePyramidAndComposite(encoder, frame, maskTex, blurStrength, frameW, frameH, outW, outH, renderTargetView);
}

// Encodes the dual-Kawase pyramid + composite using a pre-built mask texture.
// Shared between mask-aware blur (segmentation pipeline) and the no-mask
// full-frame blur (BgBlurRenderer, which binds a constant-zero mask).
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
    // WebGPU spec: importExternalTexture creates a texture bound to THIS frame's
    // GPU resource and is implicitly destroyed when the frame is closed or the
    // browser advances. NEVER cache across frames — the cached texture would
    // reference a freed resource and trigger GPU validation errors. Per-frame
    // creation is correct.
    const src = device!.importExternalTexture({ source: frame });

    const offsetMultiplier = 0.5;
    const levels = cachedLevels;
    const pyramid = getMipmapTexture(frameW, frameH);

    // Downsample: Render to mip 1, then mip 2, etc.
    let currentSrc: GPUExternalTexture | GPUTextureView = src;
    for (let level = 1; level < levels; level++) {
        const offset = getOffsetBuffer(frameW >> level, frameH >> level, blurStrength * offsetMultiplier);
        const isFirstLevel = level === 1;
        const pipeline = isFirstLevel ? mipmapDownsamplePipeline : downsample2dPipeline;

        const levelView = getPyramidView(pyramid, level);
        const mView = getMaskTextureView(maskTex);

        const pass = encoder.beginRenderPass({
            colorAttachments: [{ view: levelView, loadOp: 'clear', storeOp: 'store' }]
        });
        pass.setPipeline(pipeline);
        pass.setBindGroup(0, device!.createBindGroup({
            layout: isFirstLevel ? mipmapDownsamplePipeline.getBindGroupLayout(0) : downsample2dPipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: currentSrc },
                { binding: 1, resource: sampler },
                { binding: 2, resource: { buffer: offset } },
                { binding: 3, resource: mView },
                { binding: 4, resource: sampler }
            ]
        }));
        pass.draw(4);
        pass.end();

        currentSrc = levelView;
    }

    // Upsample: From deepest mip back to mip 0
    const maskView = getMaskTextureView(maskTex);
    for (let level = levels - 1; level > 0; level--) {
        const srcView = getPyramidView(pyramid, level);
        const targetView = getPyramidView(pyramid, level - 1);
        const offset = getOffsetBuffer(frameW >> (level - 1), frameH >> (level - 1), blurStrength * offsetMultiplier);

        const pass = encoder.beginRenderPass({
            colorAttachments: [{ view: targetView, loadOp: 'load', storeOp: 'store' }]
        });
        pass.setPipeline(mipmapUpsamplePipeline);
        pass.setBindGroup(0, device!.createBindGroup({
            layout: mipmapUpsamplePipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: srcView },
                { binding: 1, resource: sampler },
                { binding: 2, resource: { buffer: offset } },
                { binding: 3, resource: maskView },
                { binding: 4, resource: sampler }
            ]
        }));
        pass.draw(4);
        pass.end();
    }

    // Get or create output size uniform buffer (used by composite shader to scale full frame)
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

    // Composite render to the provided target (canvas or composite texture)
    const compositePass = encoder.beginRenderPass({
        colorAttachments: [{ view: renderTargetView, loadOp: 'clear', storeOp: 'store' }]
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
        ]
    }));
    compositePass.draw(4);
    compositePass.end();
}

// 1×1 mask texture filled with 0.0 → bgWeight() returns 1 for every sample
// → existing mask-aware shaders behave as a uniform full-frame blur. Lets the
// no-mask BgBlurRenderer reuse the same pipelines that the segmentation path
// uses, no shader-level branching required.
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

/**
 * Render a full-frame Kawase blur of `frame` into `targetCtx`'s current canvas
 * texture. No segmentation mask — every pixel is blurred uniformly. Caller
 * owns `frame` and must close it after this returns; this function does NOT
 * close the frame.
 */
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

/**
 * Owns a target OffscreenCanvas + its GPUCanvasContext for full-frame blur.
 * Self-initializes on first `render()` (fire-and-forget — first call may
 * no-op while WebGPU init is in flight). Used by the worker MSTG selector to
 * paint the focused tile's letterbox backdrop without CPU readback.
 */
export class BgBlurRenderer {
    private readonly canvas: OffscreenCanvas;
    private ctx: GPUCanvasContext | null = null;
    private initStarted = false;
    private initFailed = false;

    constructor(canvas: OffscreenCanvas) {
        this.canvas = canvas;
    }

    // Returns true if the blur ran, false if WebGPU isn't ready yet (or
    // initialization failed permanently). Caller does NOT lose ownership of
    // `frame`. Canvas drawing-buffer size is whatever the host set (default
    // 300×150 if no width/height attributes); CSS scales the swap-chain
    // bitmap to fill the parent via `object-fit: cover`.
    render(frame: VideoFrame, blurStrength = 4): boolean {
        if (this.initFailed)
            return false;

        if (!this.ctx) {
            this.ensureInit();
            return false;
        }

        try {
            applyFullFrameBlur(frame, this.ctx, blurStrength);
            return true;
        } catch (e) {
            warnLog?.log('BgBlurRenderer render failed:', e);
            return false;
        }
    }

    private ensureInit(): void {
        if (this.initStarted) return;
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

/**
 * Result from blur + I420 conversion including timing.
 */
export interface BlurI420Result {
    frame: VideoFrame;
    conversionTimeMs: number;
}

/**
 * Fire-and-forget blur + I420 conversion. Only awaits staging buffer availability
 * (instant at 30fps). When mapAsync resolves, calls onFrameReady with the result.
 *
 * This eliminates the ~28ms blocking readback from the processing loop.
 */
export async function submitBlurI420(
    frame: VideoFrame,
    personMask: GPUBuffer,
    maskWidth: number, maskHeight: number,
    blurStrengthOrOptions: number | BlurOptions,
    onFrameReady: (result: BlurI420Result) => void,
): Promise<void> {
    ensureInitialized();
    processBlurDeferredCleanups();

    // Parse options
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

    // Clear caches on blur strength change
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

    // Await staging buffer availability (instant at 30fps)
    await ensureStagingReady();

    // GPU work (all synchronous): blur + I420 encode + submit
    const compositeTex = getOrCreateCompositeTexture(outW, outH, FORMAT);
    const compositeView = getCompositeTextureView();
    const encoder = device!.createCommandEncoder();

    encodeBlurPasses(encoder, frame, personMask, maskWidth, maskHeight, {
        blurStrength,
        maskDirty,
        smoothingSource,
        smoothingAlpha,
    }, w, h, outW, outH, compositeView);

    encodeRGBAtoI420(encoder, compositeTex, outW, outH);
    device!.queue.submit([encoder.finish()]);

    const timestamp = frame.timestamp;
    const duration = frame.duration ?? undefined;
    deferFrameClose(frame);

    // Fire-and-forget: callback fires when mapAsync resolves
    startReadbackWithCallback(outW, outH, timestamp, duration, (videoFrame) => {
        onFrameReady({ frame: videoFrame, conversionTimeMs: 0 });
    });
}

export { awaitAllPendingReadbacks } from './webgpu-yuv-converter.js';

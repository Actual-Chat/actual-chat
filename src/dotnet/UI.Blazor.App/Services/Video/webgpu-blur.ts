import { WebGPUManager } from './webgpu-manager.js';

let device: GPUDevice | null = null;
let sampler: GPUSampler;

let downsample2dPipeline: GPURenderPipeline; // For downsampling texture_2d
let compositePipeline: GPURenderPipeline;
let mipmapDownsamplePipeline: GPURenderPipeline;
let mipmapUpsamplePipeline: GPURenderPipeline;


const offscreenCanvas = new OffscreenCanvas(1, 1);
let canvasCtx: GPUCanvasContext;

let lastBlurStrength = -1;

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
        texture.destroy();
    }
    mipmapTextureCache.clear();
};

// Texture pool with correct usage flags - keyed by dimensions
const texturePool = new Map<string, GPUTexture[]>();
const getTexture = (w: number, h: number): GPUTexture => {
    const key = `${w},${h}`;
    const pool = texturePool.get(key) || [];

    if (pool.length > 0) {
        return pool.pop()!;
    }

    return device!.createTexture({
        size: { width: w, height: h },
        format: FORMAT,
        usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING
    });
};
const returnTexture = (t: GPUTexture) => {
    const key = `${t.width},${t.height}`;
    const pool = texturePool.get(key) || [];
    pool.push(t);
    texturePool.set(key, pool);
};

const offsetBufferPool: GPUBuffer[] = [];
// Cache keys: "width,height" → buffer with correct offset values
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
    const cleanups = blurCleanupQueue.get(cleanupFrame) || [];
    cleanups.push(cleanupFn);
    blurCleanupQueue.set(cleanupFrame, cleanups);
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
                    console.warn('[WebGPUBlur] Error during deferred cleanup:', error);
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

    // Reuse from pool if possible
    let buffer: GPUBuffer;
    if (offsetBufferPool.length > 0) {
        buffer = offsetBufferPool.pop()!;
    } else {
        buffer = device!.createBuffer({
            size: 8,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
    }

    const offsetX = blurStrength / targetW;
    const offsetY = blurStrength / targetH;
  device!.queue.writeBuffer(buffer, 0, new Float32Array([offsetX, offsetY]));

  offsetBufferCache.set(key, buffer);
  return buffer;
}

function returnOffsetBuffer(buffer: GPUBuffer) {
    // Don't destroy — just return to pool
    offsetBufferPool.push(buffer);
}

// Mask cache
let cachedMaskTexture: GPUTexture | null = null;
let cachedUpscaledMask: GPUTexture | null = null;
let lastMaskW = 0;
let lastMaskH = 0;

// Pipeline for copying mask from GPUBuffer to texture (float32 input)
let maskBufferToTexturePipeline: GPUComputePipeline | null = null;


// Fixed fullscreen vertex shader (WGSL strict mode compliant)
const FULLSCREEN_VS = /* wgsl */`
  @vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
    let x = f32(i & 1u);
    let y = f32((i >> 1u) & 1u);
    return vec4f(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
  }
`;

// Mask threshold for person detection during blur
const PERSON_MASK_THRESHOLD = 0.4;

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

// Composite shader - blends original video with blurred version using mask
// Note: pos.xy already contains pixel-center coordinates (0.5, 0.5 for first pixel)
const COMPOSITE_WGSL = /* wgsl */`
  @group(0) @binding(0) var original: texture_external;
  @group(0) @binding(1) var blurred:   texture_2d<f32>;
  @group(0) @binding(2) var mask:      texture_2d<f32>;
  @group(0) @binding(3) var blurSampler: sampler;

  @fragment fn fs(@builtin(position) pos: vec4f) -> @location(0) vec4f {
      let origSize = vec2f(textureDimensions(original));

      // Use pos.xy directly (already at pixel centers) for correct alignment
      let uv       = pos.xy / origSize;

      let orig = textureSampleBaseClampToEdge(original, blurSampler, uv);
      let blur = textureSample(blurred, blurSampler, uv);
      
      // The mask is already upscaled and smoothed, sample directly with linear filtering
      let maskValue = textureSample(mask, blurSampler, uv).r;

      // Apply smoothstep for final edge refinement
      let alpha = smoothstep(0.35, 0.65, maskValue);

      // DEBUG: show full blurred background
      // return blur;

      // DEBUG: show mask only
      // return vec4f(vec3f(maskValue), 1.0);
    

      // Alpha is now 0.0-1.0 probability, use directly for blending
      // Higher probability = more person (less blur)
      return mix(blur, orig, alpha);
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
        console.log('[WebGPU-Blur] Reinitializing with new shared device');
    }
  
    device = sharedDevice;
  
    // Initialize GPU resources with the provided device
    await initializeGpuResources();
}

/**
 * Get the current WebGPU device used by the blur module
 */
export function getBlurDevice(): GPUDevice | null {
    return device;
}

// Ensure device is initialized (must call initBlurWebGPU first)
function ensureInitialized() {
    if (!device) {
        throw new Error('WebGPU blur not initialized. Call initBlurWebGPU() first.');
    }
}

// Separated GPU resource initialization (can be called with external device)
async function initializeGpuResources() {
    if (!device) throw new Error('Device not initialized');

    // Query preferred canvas format for maximum compatibility
    // This returns 'bgra8unorm' on desktop Chrome/Safari, 'rgba8unorm' on some mobile
    try {
        const gpu = navigator.gpu as GPU | undefined;
        if (gpu && typeof gpu.getPreferredCanvasFormat === 'function') {
            FORMAT = gpu.getPreferredCanvasFormat();
            console.log(`[WebGPU-Blur] Using preferred canvas format: ${FORMAT}`);
        } else {
            FORMAT = 'bgra8unorm'; // Fallback
            console.log(`[WebGPU-Blur] getPreferredCanvasFormat not available, using fallback: ${FORMAT}`);
        }
    } catch {
        FORMAT = 'bgra8unorm'; // Fallback for workers without navigator.gpu
        console.log(`[WebGPU-Blur] Using fallback canvas format: ${FORMAT}`);
    }

    sampler = WebGPUManager.getSampler();

    canvasCtx = offscreenCanvas.getContext('webgpu')!;
    canvasCtx.configure({
        device,
        format: FORMAT,
        alphaMode: 'opaque',
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
        ]
    });

    const maskUpscaleLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } }, // source mask (rgba8unorm)
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
            { binding: 2, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } }
        ]
    });

    const copyLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, sampler: {} }
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
        cachedUpscaledMask.destroy();
        cachedUpscaledMask = null;
    }
  
    // Create or reuse mask texture if dimensions match
    if (!cachedMaskTexture || lastMaskW !== w || lastMaskH !== h) {
        if (cachedMaskTexture) cachedMaskTexture.destroy();
    
        cachedMaskTexture = device!.createTexture({
            size: { width: w, height: h },
            format: 'rgba8unorm', // filterable mask format
            usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.STORAGE_BINDING
        });
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
          { binding: 1, resource: cachedMaskTexture.createView() },
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
}

// Public API - accepts either CPU Float32Array or GPU buffer
export async function applyBackgroundBlur(
    frame: VideoFrame,
    personMask: GPUBuffer,
    maskWidth: number,
    maskHeight: number,
    blurStrengthOrOptions: number | BlurOptions = 12
): Promise<VideoFrame> {
    ensureInitialized();

    // Parse options
    let blurStrength = 12;

    if (typeof blurStrengthOrOptions === 'number') {
        blurStrength = blurStrengthOrOptions;
    } else {
        blurStrength = blurStrengthOrOptions.blurStrength ?? 12;
    }

    // Clear texture pool and offset buffer cache on blur strength change
    if (blurStrength !== lastBlurStrength) {
        for (const pool of texturePool.values()) {
            for (const tex of pool) {
                tex.destroy();
            }
        }
        texturePool.clear();
        clearMipmapCache();
        lastBlurStrength = blurStrength;
    }

    const w = frame.displayWidth;
    const h = frame.displayHeight;
    if (w === 0 || h === 0)
        throw new Error('Invalid frame');

    if (offscreenCanvas.width !== w || offscreenCanvas.height !== h) {
        offscreenCanvas.width = w;
        offscreenCanvas.height = h;
    }

    const src = device!.importExternalTexture({ source: frame });
    const encoder = device!.createCommandEncoder();
  
    // Handle mask input - either CPU array or GPU buffer
    let maskTex: GPUTexture;
    const isGpuBuffer = personMask instanceof GPUBuffer ||
    (typeof personMask === 'object' && 'size' in personMask && 'usage' in personMask);
  
    // GPU buffer path - no CPU roundtrip
    maskTex = uploadMaskFromBuffer(encoder, personMask, maskWidth, maskHeight);
  
    // Pass 1: Upscale mask to video resolution FIRST (needed for mask-aware blur)
    // const upscaledMask = getUpscaledMask(encoder, maskTex, w, h);
  
    const offsetMultiplier = 0.5;

    // Dynamic pyramid blur based on blur strength
    const levels = blurStrength < 10 ? 2 : blurStrength < 20 ? 3 : 4;

    // Create single pyramid texture with all mip levels
    const pyramid = getMipmapTexture(w, h);

    // Downsample: Render to mip 1, then mip 2, etc.
    let currentSrc: GPUExternalTexture | GPUTextureView = src;
    for (let level = 1; level < levels; level++) {
        const offset = getOffsetBuffer(w >> level, h >> level, blurStrength * offsetMultiplier);
        const isFirstLevel = level === 1;
        const pipeline = isFirstLevel ? mipmapDownsamplePipeline : downsample2dPipeline;

        const pass = encoder.beginRenderPass({
            colorAttachments: [{ view: pyramid.createView({ baseMipLevel: level, mipLevelCount: 1 }), loadOp: 'clear', storeOp: 'store' }]
        });
        pass.setPipeline(pipeline);
        pass.setBindGroup(0, device!.createBindGroup({
            layout: isFirstLevel ? mipmapDownsamplePipeline.getBindGroupLayout(0) : downsample2dPipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: currentSrc },
                { binding: 1, resource: sampler },
                { binding: 2, resource: { buffer: offset } },
                { binding: 3, resource: maskTex.createView() }, // mask for person detection
                { binding: 4, resource: sampler } // mask sampler (filtering)
            ]
        }));
        pass.draw(4);
        pass.end();

        returnOffsetBuffer(offset);
        currentSrc = pyramid.createView({ baseMipLevel: level, mipLevelCount: 1 });
    }

    // Upsample: From deepest mip back to mip 0
    for (let level = levels - 1; level > 0; level--) {
        const srcView = pyramid.createView({ baseMipLevel: level, mipLevelCount: 1 });
        const targetView = pyramid.createView({ baseMipLevel: level - 1, mipLevelCount: 1 });
        const offset = getOffsetBuffer(w >> (level - 1), h >> (level - 1), blurStrength * offsetMultiplier);

        const pass = encoder.beginRenderPass({
            colorAttachments: [{ view: targetView, loadOp: 'load', storeOp: 'store' }] // Load to blend/accumulate if needed
        });
        pass.setPipeline(mipmapUpsamplePipeline);
        pass.setBindGroup(0, device!.createBindGroup({
            layout: mipmapUpsamplePipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: srcView },
                { binding: 1, resource: sampler },
                { binding: 2, resource: { buffer: offset } },
                { binding: 3, resource: maskTex.createView() }, // mask for person detection
                { binding: 4, resource: sampler } // mask sampler (filtering)
            ]
        }));
        pass.draw(4);
        pass.end();

        returnOffsetBuffer(offset);
    }

    // Composite uses pyramid.createView({ baseMipLevel: 0 }) as blurred

    // Pass 4: Composite render to canvas
    const compositePass = encoder.beginRenderPass({
        colorAttachments: [{ view: canvasCtx.getCurrentTexture().createView(), loadOp: 'clear', storeOp: 'store' }]
    });
    compositePass.setPipeline(compositePipeline);
    compositePass.setBindGroup(0, device!.createBindGroup({
        layout: compositePipeline.getBindGroupLayout(0),
        entries: [
            { binding: 0, resource: src },
            { binding: 1, resource: pyramid.createView({ baseMipLevel: 0, mipLevelCount: 1 }) },
            { binding: 2, resource: maskTex.createView() },
            { binding: 3, resource: sampler }, // filtering sampler for both blurred and mask
        ]
    }));
    compositePass.draw(4);
    compositePass.end();

    // Submit work with proper synchronization to avoid GPU contention
    const commandBuffer = encoder.finish();
  device!.queue.submit([commandBuffer]);

  // Add synchronization to yield GPU resources for video decoding
  // This helps prevent WebGPU inference from hogging the GPU command queue
  // On Android mobile devices, this is critical for maintaining video decoding performance
  if (typeof device!.queue.onSubmittedWorkDone === 'function') {
      await device!.queue.onSubmittedWorkDone();
  } else {
      // Fallback: yield to event loop to allow video decoding to interleave
      // This provides basic synchronization when onSubmittedWorkDone is not available
      await new Promise(resolve => setTimeout(resolve, 0));
  }

  // Clean up
  returnTexture(pyramid);
  const timestamp = frame.timestamp;
  frame.close();

  return new VideoFrame(offscreenCanvas, { timestamp });
}

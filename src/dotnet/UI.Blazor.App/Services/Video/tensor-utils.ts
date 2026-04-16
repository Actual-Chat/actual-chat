/**
 * Tensor Utilities for ONNX Runtime Web with WebGPU
 * Creates WebGPU buffer-backed tensors for zero-copy inference
 */

import * as ort from 'onnxruntime-web';
import { WebGPUManager } from './webgpu-manager.js';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoSegmentation');

// WebGPU state (shared through WebGPUManager)
let device: GPUDevice | null = null;
let sampler: GPUSampler | null = null;
let tensorCopyPipeline: GPURenderPipeline | null = null;
let rgbaToRgbUint8Pipeline: GPUComputePipeline | null = null;
let rgbaToFloat32Pipeline: GPUComputePipeline | null = null;

// Buffer pools for tensor conversion
const tensorTexturePool = new Map<string, GPUTexture[]>();
const tensorBufferPool = new Map<number, GPUBuffer[]>();

// Uniform buffer pool for 8-byte buffers (reduces per-frame allocation overhead)
const uniformBuffer8Pool: GPUBuffer[] = [];
// Uniform buffer pool for 16-byte buffers (for NCHW shader params)
const uniformBuffer16Pool: GPUBuffer[] = [];

// Pre-allocated TypedArrays to avoid per-frame allocations
const tempFloat32Array2 = new Float32Array(2);
const tempUint32Array2 = new Uint32Array(2);
const tempFloat32Array4 = new Float32Array(4);

// Deferred cleanup system to eliminate sync points
let frameCounter = 0;
const cleanupQueue = new Map<number, (() => void)[]>();

// Frames to wait before cleaning up resources (balances memory usage vs sync overhead)
const CLEANUP_DELAY_FRAMES = 3;

/**
 * Get a pooled 8-byte uniform buffer
 */
function getUniformBuffer8(): GPUBuffer {
    if (uniformBuffer8Pool.length > 0) {
        return uniformBuffer8Pool.pop()!;
    }
    return device!.createBuffer({
        size: 8,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
    });
}

/**
 * Return an 8-byte uniform buffer to the pool
 */
function returnUniformBuffer8(buffer: GPUBuffer): void {
    uniformBuffer8Pool.push(buffer);
}

/**
 * Get a pooled 16-byte uniform buffer
 */
function getUniformBuffer16(): GPUBuffer {
    if (uniformBuffer16Pool.length > 0) {
        return uniformBuffer16Pool.pop()!;
    }
    return device!.createBuffer({
        size: 16,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
    });
}

/**
 * Return a 16-byte uniform buffer to the pool
 */
function returnUniformBuffer16(buffer: GPUBuffer): void {
    uniformBuffer16Pool.push(buffer);
}

// Debug flag to allow slow GPU→CPU tensor downloads in development builds only
const ENABLE_DEBUG_TENSOR_DOWNLOAD =
  typeof import.meta !== 'undefined' && // eslint-disable-next-line
  typeof (import.meta as any).env !== 'undefined' && // eslint-disable-next-line
  Boolean((import.meta as any).env?.DEV);

const FULLSCREEN_VS = /* wgsl */`
  @vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
    let x = f32(i & 1u);
    let y = f32((i >> 1u) & 1u);
    return vec4f(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
  }
`;

/**
 * Initialize WebGPU for tensor operations
 */
export async function initTensorWebGPU(gpuDevice?: GPUDevice): Promise<GPUDevice> {
    if (device) return device;

    device = await WebGPUManager.init(gpuDevice);
    sampler = WebGPUManager.getSampler();


    // Shader to copy and resize VideoFrame to RGBA texture
    // Note: pos.xy already contains pixel-center coordinates (0.5, 0.5 for first pixel)
    // so we divide directly by targetSize to get correct UV coordinates
    const tensorCopyShader = /* wgsl */`
    @group(0) @binding(0) var src: texture_external;
    @group(0) @binding(1) var s: sampler;
    @group(0) @binding(2) var<uniform> targetSize: vec2f;

    @fragment fn fs(@builtin(position) pos: vec4f) -> @location(0) vec4f {
      let uv = pos.xy / targetSize;
      let color = textureSampleBaseClampToEdge(src, s, uv);
      // Return as RGBA (no swizzle needed for rgba8unorm)
      return vec4f(color.r, color.g, color.b, 1.0);
    }
  `;

    const tensorCopyModule = device.createShaderModule({ code: tensorCopyShader });
    tensorCopyPipeline = device.createRenderPipeline({
        layout: 'auto',
        vertex: { module: device.createShaderModule({ code: FULLSCREEN_VS }), entryPoint: 'vs' },
        fragment: { module: tensorCopyModule, entryPoint: 'fs', targets: [{ format: 'rgba8unorm' }] },
        primitive: { topology: 'triangle-strip' }
    });

    // Compute shader to convert RGBA texture to RGB buffer (uint8)
    // Output is [1, height, width, 3] packed as uint8 bytes (single frame)
    // Each u32 in the output buffer contains 4 packed bytes
    const rgbaToRgbUint8Shader = /* wgsl */`
    @group(0) @binding(0) var src: texture_2d<f32>;
    @group(0) @binding(1) var<storage, read_write> dst: array<u32>;
    @group(0) @binding(2) var<uniform> params: vec2u; // width, height

    @compute @workgroup_size(256)
    fn main(@builtin(global_invocation_id) id: vec3<u32>) {
      let width = params.x;
      let height = params.y;
      let bytesTotal = width * height * 3u;

      // Each thread processes one u32 (4 packed bytes)
      let u32sTotal = (bytesTotal + 3u) / 4u;

      if (id.x >= u32sTotal) { return; }

      // Byte index within the output
      let byteStart = id.x * 4u;

      var packed: u32 = 0u;

      // Pack up to 4 bytes into this u32
      for (var i = 0u; i < 4u; i++) {
        let byteIndex = byteStart + i;
        if (byteIndex >= bytesTotal) { break; }

        // Calculate which pixel and channel this byte represents
        let pixelIndex = byteIndex / 3u;
        let channel = byteIndex % 3u;

        let x = pixelIndex % width;
        let y = pixelIndex / width;

        let color = textureLoad(src, vec2u(x, y), 0);
        var byteVal: u32;
        if (channel == 0u) {
          byteVal = u32(color.r * 255.0);
        } else if (channel == 1u) {
          byteVal = u32(color.g * 255.0);
        } else {
          byteVal = u32(color.b * 255.0);
        }

        // Pack byte into the correct position (little-endian)
        packed = packed | (byteVal << (i * 8u));
      }

      dst[id.x] = packed;
    }
  `;

    const rgbaToRgbModule = device.createShaderModule({ code: rgbaToRgbUint8Shader });
    rgbaToRgbUint8Pipeline = device.createComputePipeline({
        layout: 'auto',
        compute: { module: rgbaToRgbModule, entryPoint: 'main' }
    });

    // Compute shader to convert RGBA texture to NCHW float32 buffer
    // Output is [1, 3, height, width] planar layout (channels-first)
    // Each plane contains height*width float32 values
    const rgbaToRgbFloat32Shader = /* wgsl */`
    @group(0) @binding(0) var src: texture_2d<f32>;
    @group(0) @binding(1) var<storage, read_write> dst: array<f32>;
    @group(0) @binding(2) var<uniform> params: vec4<f32>;  // width, height (pixels); remaining components unused

    @compute @workgroup_size(16, 16)
    fn main(@builtin(global_invocation_id) id: vec3<u32>) {
      let width = u32(params.x);
      let height = u32(params.y);

      if (id.x >= width || id.y >= height) { return; }

      let color = textureLoad(src, vec2<i32>(id.xy), 0);
      let pixelIndex = id.y * width + id.x;
      let planeSize = width * height;

      // Write raw float32 color channels in [0, 1] range to NCHW planes
      dst[pixelIndex] = color.r;                    // R plane
      dst[pixelIndex + planeSize] = color.g;        // G plane
      dst[pixelIndex + 2u * planeSize] = color.b;   // B plane
    }
  `;

    const rgbaToNchwModule = device.createShaderModule({ code: rgbaToRgbFloat32Shader });
    rgbaToFloat32Pipeline = device.createComputePipeline({
        layout: 'auto',
        compute: { module: rgbaToNchwModule, entryPoint: 'main' }
    });


    return device;
}

/**
 * Get or create a pooled texture
 */
function getPooledTexture(width: number, height: number): GPUTexture {
    const key = `${width}x${height}`;
    const pool = tensorTexturePool.get(key) ?? [];

    if (pool.length > 0) {
        return pool.pop()!;
    }

    return device!.createTexture({
        size: { width, height },
        format: 'rgba8unorm',
        usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.STORAGE_BINDING
    });
}

/**
 * Return a texture to the pool
 */
function returnPooledTexture(texture: GPUTexture): void {
    const key = `${texture.width}x${texture.height}`;
    const pool = tensorTexturePool.get(key) ?? [];
    pool.push(texture);
    tensorTexturePool.set(key, pool);
}

/**
 * Get or create a pooled buffer
 */
function getPooledBuffer(size: number, usage: GPUBufferUsageFlags): GPUBuffer {
    const key = size;
    const pool = tensorBufferPool.get(key) ?? [];

    if (pool.length > 0) {
        return pool.pop()!;
    }

    return device!.createBuffer({ size, usage });
}

/**
 * Return a buffer to the pool
 */
export function returnPooledBuffer(buffer: GPUBuffer): void {
    const key = buffer.size;
    const pool = tensorBufferPool.get(key) ?? [];
    pool.push(buffer);
    tensorBufferPool.set(key, pool);
}

/**
 * Register a cleanup function to be executed after GPU work is done
 * Eliminates the need for onSubmittedWorkDone() sync points
 */
function registerDeferredCleanup(cleanupFn: () => void): void {
    const cleanupFrame = frameCounter + CLEANUP_DELAY_FRAMES;
    const cleanups = cleanupQueue.get(cleanupFrame) ?? [];
    cleanups.push(cleanupFn);
    cleanupQueue.set(cleanupFrame, cleanups);
}

/**
 * Process cleanups for completed frames
 * Call this periodically to clean up resources without blocking
 */
export function processDeferredCleanups(currentFrame: number = ++frameCounter): void {
    // Clean up frames that are sufficiently old
    const minCleanupFrame = currentFrame - CLEANUP_DELAY_FRAMES;
    for (const [frameNum, cleanups] of cleanupQueue) {
        if (frameNum <= minCleanupFrame) {
            for (const cleanup of cleanups) {
                try {
                    cleanup();
                } catch (error) {
                    warnLog?.log('Error during deferred cleanup:', error);
                }
            }
            cleanupQueue.delete(frameNum);
        }
    }
}

/**
 * Convert a single VideoFrame to a WebGPU buffer-backed ONNX tensor
 * All processing stays on GPU - no CPU roundtrip
 *
 * @param frame VideoFrame to convert
 * @param targetWidth Target width for the model
 * @param targetHeight Target height for the model
 * @returns WebGPU buffer-backed ONNX tensor with shape [1, height, width, 3] and uint8 values
 */
export function videoFrameToTensorUint8(
    frame: VideoFrame,
    targetWidth: number,
    targetHeight: number
): Promise<ort.Tensor> {
    if (!device) {
    // CPU fallback path for non-WebGPU backends (e.g., WASM)
        const canvas = new OffscreenCanvas(targetWidth, targetHeight);
        const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
        ctx.drawImage(frame, 0, 0, targetWidth, targetHeight);
        const imageData = ctx.getImageData(0, 0, targetWidth, targetHeight);
        const data = imageData.data;
        const pixelCount = targetWidth * targetHeight;
        const rgbData = new Uint8Array(pixelCount * 3);
        let j = 0;
        for (let i = 0; i < data.length; i += 4, j += 3) {
            rgbData[j] = data[i];     // R
            rgbData[j + 1] = data[i + 1]; // G
            rgbData[j + 2] = data[i + 2]; // B
        }
        return Promise.resolve(new ort.Tensor('uint8', rgbData, [1, targetHeight, targetWidth, 3]));
    }

    const pixelCount = targetWidth * targetHeight;
    const totalBytes = pixelCount * 3; // RGB

    // Round up to 4-byte alignment for WebGPU
    const alignedTotalBytes = Math.ceil(totalBytes / 4) * 4;

    // Create or reuse output GPU buffer for the tensor
    // Use STORAGE for compute shader write, COPY_SRC for potential readback
    const tensorBuffer = getPooledBuffer(
        alignedTotalBytes,
        GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST
    );

    // Get intermediate texture
    const intermediateTexture = getPooledTexture(targetWidth, targetHeight);

    // Import VideoFrame as external texture
    const externalTex = device.importExternalTexture({ source: frame });

    // Get pooled uniform buffer for target size (reduces allocation overhead)
    const sizeBuffer = getUniformBuffer8();
    tempFloat32Array2[0] = targetWidth;
    tempFloat32Array2[1] = targetHeight;
    device.queue.writeBuffer(sizeBuffer, 0, tempFloat32Array2);

    // Render pass to copy/resize VideoFrame to intermediate texture
    const encoder = device.createCommandEncoder();

    const renderPass = encoder.beginRenderPass({
        colorAttachments: [{
            view: intermediateTexture.createView(),
            loadOp: 'clear',
            storeOp: 'store'
        }]
    });
    renderPass.setPipeline(tensorCopyPipeline!);
    renderPass.setBindGroup(0, device.createBindGroup({
        layout: tensorCopyPipeline!.getBindGroupLayout(0),
        entries: [
            { binding: 0, resource: externalTex },
            { binding: 1, resource: sampler! },
            { binding: 2, resource: { buffer: sizeBuffer } }
        ]
    }));
    renderPass.draw(4);
    renderPass.end();

    // Get pooled uniform buffer for compute shader params (width, height)
    const paramsBuffer = getUniformBuffer8();
    tempUint32Array2[0] = targetWidth;
    tempUint32Array2[1] = targetHeight;
    device.queue.writeBuffer(paramsBuffer, 0, tempUint32Array2);

    // Compute pass to convert RGBA to RGB and write to tensor buffer
    const computePass = encoder.beginComputePass();
    computePass.setPipeline(rgbaToRgbUint8Pipeline!);
    computePass.setBindGroup(0, device.createBindGroup({
        layout: rgbaToRgbUint8Pipeline!.getBindGroupLayout(0),
        entries: [
            { binding: 0, resource: intermediateTexture.createView() },
            { binding: 1, resource: { buffer: tensorBuffer } },
            { binding: 2, resource: { buffer: paramsBuffer } }
        ]
    }));

    // Each thread processes 4 bytes (1 u32), workgroup size is 256
    const u32sTotal = Math.ceil(totalBytes / 4);
    const workgroups = Math.ceil(u32sTotal / 256);
    computePass.dispatchWorkgroups(workgroups);
    computePass.end();

    device.queue.submit([encoder.finish()]);

    // Schedule deferred cleanup (no sync point)
    registerDeferredCleanup(() => {
        returnUniformBuffer8(sizeBuffer);
        returnUniformBuffer8(paramsBuffer);
        returnPooledTexture(intermediateTexture);
    });

    // NOTE: We intentionally don't await onSubmittedWorkDone() here.
    // ONNX Runtime WebGPU backend shares the same device and handles
    // GPU command dependencies internally via WebGPU's implicit synchronization.
    // Removing this sync saves 1-5ms per frame.

    // Optional debug hook for forcing GPU→CPU readback
    const downloadCallback = ENABLE_DEBUG_TENSOR_DOWNLOAD
        ? async () => {
            const readBuffer = device!.createBuffer({
                size: alignedTotalBytes,
                usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ
            });

            const encoder = device!.createCommandEncoder();
            encoder.copyBufferToBuffer(tensorBuffer, 0, readBuffer, 0, alignedTotalBytes);
        device!.queue.submit([encoder.finish()]);

        await readBuffer.mapAsync(GPUMapMode.READ);
        const data = new Uint8Array(readBuffer.getMappedRange()).slice(0, totalBytes);
        readBuffer.unmap();
        readBuffer.destroy();

        return data;
        }
        : undefined;

    // NHWC layout assumption: compute shader writes [1, H, W, 3] so the model must consume NHWC input
    const tensor = ort.Tensor.fromGpuBuffer(tensorBuffer, {
        dataType: 'uint8',
        dims: [1, targetHeight, targetWidth, 3],
        ...(downloadCallback ? { download: downloadCallback } : {}),
        dispose: () => {
            returnPooledBuffer(tensorBuffer);
        }
    });

    return Promise.resolve(tensor);
}

/**
 * Convert a single VideoFrame to a WebGPU buffer-backed ONNX tensor in NCHW format
 * All processing stays on GPU - no CPU roundtrip
 * Output is float32 with planar layout: [R-plane][G-plane][B-plane]
 * Pixels are assumed to already be in the [0, 1] range as provided by the GPU texture.
 *
 * @param frame VideoFrame to convert
 * @param targetWidth Target width for the model
 * @param targetHeight Target height for the model
 * @returns WebGPU buffer-backed ONNX tensor with shape [1, 3, height, width] and float32 values
 */
export function videoFrameToTensorFloat32(
    frame: VideoFrame,
    targetWidth: number,
    targetHeight: number
): Promise<ort.Tensor> {
    if (!device) {
    // CPU fallback path for non-WebGPU backends (e.g., WASM)
        const canvas = new OffscreenCanvas(targetWidth, targetHeight);
        const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
        ctx.drawImage(frame, 0, 0, targetWidth, targetHeight);
        const imageData = ctx.getImageData(0, 0, targetWidth, targetHeight);
        const data = imageData.data;
        const pixelCount = targetWidth * targetHeight;
        const planeSize = pixelCount;
        const floatData = new Float32Array(3 * planeSize);
        let k = 0;
        for (let i = 0; i < data.length; i += 4, k++) {
            floatData[k] = data[i] / 255.0;           // R plane
            floatData[k + planeSize] = data[i + 1] / 255.0; // G plane
            floatData[k + 2 * planeSize] = data[i + 2] / 255.0; // B plane
        }
        return Promise.resolve(new ort.Tensor('float32', floatData, [1, 3, targetHeight, targetWidth]));
    }

    const pixelCount = targetWidth * targetHeight;
    // NCHW float32: 3 channels * height * width * 4 bytes per float
    const totalBytes = 3 * pixelCount * 4;

    // Create or reuse output GPU buffer for the tensor
    // Use STORAGE for compute shader write, COPY_SRC for potential readback
    const tensorBuffer = getPooledBuffer(
        totalBytes,
        GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST
    );

    // Get intermediate texture
    const intermediateTexture = getPooledTexture(targetWidth, targetHeight);

    // Import VideoFrame as external texture
    const externalTex = device.importExternalTexture({ source: frame });

    // Get pooled uniform buffer for target size (reduces allocation overhead)
    const sizeBuffer = getUniformBuffer8();
    tempFloat32Array2[0] = targetWidth;
    tempFloat32Array2[1] = targetHeight;
    device.queue.writeBuffer(sizeBuffer, 0, tempFloat32Array2);

    // Render pass to copy/resize VideoFrame to intermediate texture
    const encoder = device.createCommandEncoder();

    const renderPass = encoder.beginRenderPass({
        colorAttachments: [{
            view: intermediateTexture.createView(),
            loadOp: 'clear',
            storeOp: 'store'
        }]
    });
    renderPass.setPipeline(tensorCopyPipeline!);
    renderPass.setBindGroup(0, device.createBindGroup({
        layout: tensorCopyPipeline!.getBindGroupLayout(0),
        entries: [
            { binding: 0, resource: externalTex },
            { binding: 1, resource: sampler! },
            { binding: 2, resource: { buffer: sizeBuffer } }
        ]
    }));
    renderPass.draw(4);
    renderPass.end();

    // Get pooled uniform buffer for compute shader params (width, height)
    const paramsBuffer = getUniformBuffer16();
    tempFloat32Array4[0] = targetWidth;
    tempFloat32Array4[1] = targetHeight;
    tempFloat32Array4[2] = 0;
    tempFloat32Array4[3] = 0;
    device.queue.writeBuffer(paramsBuffer, 0, tempFloat32Array4);

    // Use the float32 compute shader to convert RGBA to NCHW float32
    const computePass = encoder.beginComputePass();
    computePass.setPipeline(rgbaToFloat32Pipeline!);
    computePass.setBindGroup(0, device.createBindGroup({
        layout: rgbaToFloat32Pipeline!.getBindGroupLayout(0),
        entries: [
            { binding: 0, resource: intermediateTexture.createView() },
            { binding: 1, resource: { buffer: tensorBuffer } },
            { binding: 2, resource: { buffer: paramsBuffer } }
        ]
    }));

    // Each thread processes one pixel, workgroup size is 16x16
    const workgroupsX = Math.ceil(targetWidth / 16);
    const workgroupsY = Math.ceil(targetHeight / 16);
    computePass.dispatchWorkgroups(workgroupsX, workgroupsY);
    computePass.end();

    device.queue.submit([encoder.finish()]);

    // Schedule deferred cleanup (no sync point)
    registerDeferredCleanup(() => {
        returnUniformBuffer8(sizeBuffer);
        returnUniformBuffer16(paramsBuffer);
        returnPooledTexture(intermediateTexture);
    });

    // NOTE: We intentionally don't await onSubmittedWorkDone() here.
    // ONNX Runtime WebGPU backend shares the same device and handles
    // GPU command dependencies internally via WebGPU's implicit synchronization.
    // Removing this sync saves 1-5ms per frame.

    // Optional debug hook for forcing GPU→CPU readback
    const downloadCallback = ENABLE_DEBUG_TENSOR_DOWNLOAD
        ? async () => {
            const readBuffer = device!.createBuffer({
                size: totalBytes,
                usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ
            });

            const encoder = device!.createCommandEncoder();
            encoder.copyBufferToBuffer(tensorBuffer, 0, readBuffer, 0, totalBytes);
        device!.queue.submit([encoder.finish()]);

        await readBuffer.mapAsync(GPUMapMode.READ);
        const float32Data = new Float32Array(readBuffer.getMappedRange().slice(0));
        readBuffer.unmap();
        readBuffer.destroy();

        return float32Data;
        }
        : undefined;

    // NCHW layout: [1, 3, height, width] - channels first (planar)
    // Create float32 tensor - buffer contains float32 data from GPU shader
    // ONNX Runtime will handle conversion to float16 if the model requires it
    const tensor = ort.Tensor.fromGpuBuffer(tensorBuffer, {
        dataType: 'float32',
        dims: [1, 3, targetHeight, targetWidth],
        ...(downloadCallback ? { download: downloadCallback } : {}),
        dispose: () => {
            returnPooledBuffer(tensorBuffer);
        }
    });

    return Promise.resolve(tensor);
}

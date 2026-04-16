/**
 * WebGPU RGBA → I420 Converter
 *
 * Uses a WGSL compute shader to convert the composite RGBA/BGRA texture
 * (output of the blur pipeline) directly to I420 planar format on the GPU.
 * This avoids the slow CPU-side VideoFrame.copyTo({ format: 'I420' }) which
 * throws NotSupportedError on Android (RGBA) and Windows (BGRA).
 *
 * Architecture:
 *   GPU composite texture → compute shader → I420 staging buffer → mapAsync → VideoFrame(I420)
 *
 * Optimizations over naive approach:
 *   - Packed bytes (4 pixels per u32) — 4× less GPU→CPU transfer vs u32-per-byte
 *   - Single staging buffer for Y+U+V — 1 mapAsync instead of 3
 *   - Uint8Array.set() bulk copy instead of per-byte extraction loops
 *
 * Each compute thread handles one pixel (Y) and optionally one chroma sample
 * (U+V for top-left pixel of each 2×2 block). Packed byte writes use atomicOr
 * on pre-cleared buffers.
 */

import { getLogs } from 'logging';

const { infoLog, warnLog } = getLogs('VideoSegmentation');

// ── WGSL Compute Shader (packed byte output) ────────────────────────────────

const RGBA_TO_I420_WGSL = /* wgsl */`
  @group(0) @binding(0) var src: texture_2d<f32>;
  @group(0) @binding(1) var<storage, read_write> yPlane: array<atomic<u32>>;
  @group(0) @binding(2) var<storage, read_write> uPlane: array<atomic<u32>>;
  @group(0) @binding(3) var<storage, read_write> vPlane: array<atomic<u32>>;
  @group(0) @binding(4) var<uniform> params: vec4u; // width, height, chromaW, chromaH

  @compute @workgroup_size(16, 16)
  fn main(@builtin(global_invocation_id) gid: vec3u) {
    let width   = params.x;
    let height  = params.y;
    let chromaW = params.z;

    if (gid.x >= width || gid.y >= height) { return; }

    let c = textureLoad(src, vec2u(gid.x, gid.y), 0);

    // BT.601 limited-range: Y=[16,235]
    let yVal = u32(clamp(round(65.481 * c.r + 128.553 * c.g + 24.966 * c.b + 16.0), 0.0, 255.0));

    // Pack Y byte into u32 word (buffers pre-cleared to 0, atomicOr is safe)
    let yIdx = gid.y * width + gid.x;
    atomicOr(&yPlane[yIdx >> 2u], yVal << ((yIdx & 3u) << 3u));

    // Chroma: one sample per 2×2 block (top-left pixel of each block)
    if ((gid.x & 1u) == 0u && (gid.y & 1u) == 0u) {
      let x1 = min(gid.x + 1u, width  - 1u);
      let y1 = min(gid.y + 1u, height - 1u);

      let c10 = textureLoad(src, vec2u(x1,    gid.y), 0);
      let c01 = textureLoad(src, vec2u(gid.x, y1),    0);
      let c11 = textureLoad(src, vec2u(x1,    y1),    0);

      // Average 2×2 block for chroma subsampling
      let avg = (c + c10 + c01 + c11) * 0.25;

      // BT.601 limited-range: UV=[16,240]
      let uVal = u32(clamp(round(-37.797 * avg.r -  74.203 * avg.g + 112.0   * avg.b + 128.0), 0.0, 255.0));
      let vVal = u32(clamp(round( 112.0  * avg.r -  93.786 * avg.g -  18.214 * avg.b + 128.0), 0.0, 255.0));

      let cIdx = (gid.y >> 1u) * chromaW + (gid.x >> 1u);
      atomicOr(&uPlane[cIdx >> 2u], uVal << ((cIdx & 3u) << 3u));
      atomicOr(&vPlane[cIdx >> 2u], vVal << ((cIdx & 3u) << 3u));
    }
  }
`;

// ── Module State ────────────────────────────────────────────────────────────────

let device: GPUDevice | null = null;
let yuvPipeline: GPUComputePipeline | null = null;

// Composite texture (render target for blur → input for YUV compute)
let compositeTexture: GPUTexture | null = null;
let compositeTextureView: GPUTextureView | null = null;
let compositeKey = '';

// GPU storage buffers (packed bytes, 4× smaller than u32-per-byte)
let bufferKey = '';
let yStorageBuf: GPUBuffer | null = null;
let uStorageBuf: GPUBuffer | null = null;
let vStorageBuf: GPUBuffer | null = null;

// Double-buffered staging: GPU writes to one while CPU reads from the other
let stagingBufs: [GPUBuffer | null, GPUBuffer | null] = [null, null];
let currentStagingIdx = 0;

// Per-buffer readiness: resolves when the buffer is unmapped and available for GPU writes
let stagingReadyPromises: [Promise<void>, Promise<void>] = [Promise.resolve(), Promise.resolve()];

let paramsBuf: GPUBuffer | null = null;

// Cached bind group for encodeRGBAtoI420 (all resources stable per-resolution)
let cachedYuvBindGroup: GPUBindGroup | null = null;

// Packed byte sizes for current resolution
let yPackedSize = 0;
let uvPackedSize = 0;

// ── Public API ──────────────────────────────────────────────────────────────────

/**
 * Initialize the YUV converter pipeline.
 * Must be called after the WebGPU device is available (from initBlurWebGPU).
 */
export function initYUVConverter(gpuDevice: GPUDevice): void {
    device = gpuDevice;

    const module = device.createShaderModule({ code: RGBA_TO_I420_WGSL });
    yuvPipeline = device.createComputePipeline({
        layout: 'auto',
        compute: { module, entryPoint: 'main' },
    });

    infoLog?.log('WebGPU YUV converter initialized (packed byte output)');
}

/**
 * Get or create the composite texture used as an intermediate render target.
 * Has RENDER_ATTACHMENT | TEXTURE_BINDING usage so it can be both a render target
 * and an input to the compute shader.
 */
export function getOrCreateCompositeTexture(
    width: number,
    height: number,
    format: GPUTextureFormat,
): GPUTexture {
    const key = `${width},${height},${format}`;
    if (compositeKey === key && compositeTexture) {
        return compositeTexture;
    }

    // Destroy previous
    if (compositeTexture) {
        compositeTexture.destroy();
    }

    compositeTexture = device!.createTexture({
        size: { width, height },
        format,
        usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING,
    });
    compositeTextureView = compositeTexture.createView();
    compositeKey = key;

    return compositeTexture;
}

/**
 * Get the cached view for the current composite texture.
 */
export function getCompositeTextureView(): GPUTextureView {
    if (!compositeTextureView) {
        throw new Error('Composite texture not created yet');
    }
    return compositeTextureView;
}

// ── Internal Helpers ────────────────────────────────────────────────────────────

function ensureBuffers(width: number, height: number): void {
    const key = `${width},${height}`;
    if (bufferKey === key) return;

    // Invalidate cached bind group on resize
    cachedYuvBindGroup = null;

    // Destroy old buffers
    yStorageBuf?.destroy();
    uStorageBuf?.destroy();
    vStorageBuf?.destroy();
    stagingBufs[0]?.destroy();
    stagingBufs[1]?.destroy();
    paramsBuf?.destroy();

    const ySize = width * height;
    const chromaW = Math.ceil(width / 2);
    const chromaH = Math.ceil(height / 2);
    const uvSize = chromaW * chromaH;

    // Packed byte sizes: ceil to 4-byte alignment for copyBufferToBuffer
    yPackedSize = Math.ceil(ySize / 4) * 4;
    uvPackedSize = Math.ceil(uvSize / 4) * 4;
    const totalStagingSize = yPackedSize + uvPackedSize * 2;

    // Storage buffers: packed bytes (4 pixels per u32)
    // COPY_DST needed for clearBuffer, COPY_SRC needed for copyBufferToBuffer
    const storageUsage = GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST;
    yStorageBuf = device!.createBuffer({ size: yPackedSize, usage: storageUsage });
    uStorageBuf = device!.createBuffer({ size: uvPackedSize, usage: storageUsage });
    vStorageBuf = device!.createBuffer({ size: uvPackedSize, usage: storageUsage });

    // Double-buffered staging: one for GPU writes, one for CPU reads
    const stagingUsage = GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ;
    stagingBufs = [
        device!.createBuffer({ size: totalStagingSize, usage: stagingUsage }),
        device!.createBuffer({ size: totalStagingSize, usage: stagingUsage }),
    ];
    currentStagingIdx = 0;
    stagingReadyPromises = [Promise.resolve(), Promise.resolve()];

    // Uniform params buffer: vec4u (width, height, chromaW, chromaH)
    paramsBuf = device!.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    const paramsData = new Uint32Array([width, height, chromaW, chromaH]);
    device!.queue.writeBuffer(paramsBuf, 0, paramsData);

    bufferKey = key;
}

/**
 * Append the RGBA→I420 compute pass and buffer copy commands to an existing encoder.
 * Does NOT submit — caller is responsible for submitting.
 */
export function encodeRGBAtoI420(
    encoder: GPUCommandEncoder,
    sourceTexture: GPUTexture,
    width: number,
    height: number,
): void {
    if (!yuvPipeline) throw new Error('YUV converter not initialized');

    ensureBuffers(width, height);

    // Clear storage buffers to zero (required for atomicOr correctness)
    encoder.clearBuffer(yStorageBuf!);
    encoder.clearBuffer(uStorageBuf!);
    encoder.clearBuffer(vStorageBuf!);

    // Compute pass: one thread per pixel
    const pass = encoder.beginComputePass();
    pass.setPipeline(yuvPipeline);

    // Cache bind group — all resources are stable per-resolution
    // Use compositeTextureView when available (production path), fall back to createView
    const texView = compositeTextureView && sourceTexture === compositeTexture
        ? compositeTextureView
        : sourceTexture.createView();

    if (!cachedYuvBindGroup || texView !== compositeTextureView) {
        cachedYuvBindGroup = device!.createBindGroup({
            layout: yuvPipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: texView },
                { binding: 1, resource: { buffer: yStorageBuf! } },
                { binding: 2, resource: { buffer: uStorageBuf! } },
                { binding: 3, resource: { buffer: vStorageBuf! } },
                { binding: 4, resource: { buffer: paramsBuf! } },
            ],
        });
    }
    pass.setBindGroup(0, cachedYuvBindGroup);
    pass.dispatchWorkgroups(Math.ceil(width / 16), Math.ceil(height / 16));
    pass.end();

    // Copy all planes to current staging buffer (Y | U | V)
    const staging = stagingBufs[currentStagingIdx]!;
    encoder.copyBufferToBuffer(yStorageBuf!, 0, staging, 0, yPackedSize);
    encoder.copyBufferToBuffer(uStorageBuf!, 0, staging, yPackedSize, uvPackedSize);
    encoder.copyBufferToBuffer(vStorageBuf!, 0, staging, yPackedSize + uvPackedSize, uvPackedSize);
}

/**
 * Await until the staging buffer we're about to write to is available.
 * Instant in steady state at 30fps (buffer reuse every 2 frames = 66ms, far exceeding ~28ms GPU time).
 */
export async function ensureStagingReady(): Promise<void> {
    await stagingReadyPromises[currentStagingIdx];
}

/**
 * Non-blocking readback. Starts mapAsync and calls onReady when data is available.
 * Must be called AFTER the command encoder containing encodeRGBAtoI420 has been submitted.
 * Flips the staging buffer index so the next frame writes to the other buffer.
 */
export function startReadbackWithCallback(
    width: number, height: number, timestamp: number, duration: number | undefined,
    onReady: (frame: VideoFrame) => void,
): void {
    const bufIdx = currentStagingIdx;
    const staging = stagingBufs[bufIdx]!;

    // Mark buffer as busy — ensureStagingReady will await this
    let resolveReady!: () => void;
    stagingReadyPromises[bufIdx] = new Promise(r => resolveReady = r);

    const mapPromise = staging.mapAsync(GPUMapMode.READ);
    currentStagingIdx = 1 - currentStagingIdx;

    mapPromise.then(() => {
        try {
            const mapped = new Uint8Array(staging.getMappedRange());
            const chromaW = Math.ceil(width / 2);
            const frame = new VideoFrame(mapped.buffer, {
                format: 'I420',
                codedWidth: width,
                codedHeight: height,
                timestamp,
                duration,
                layout: [
                    { offset: mapped.byteOffset, stride: width },
                    { offset: mapped.byteOffset + yPackedSize, stride: chromaW },
                    { offset: mapped.byteOffset + yPackedSize + uvPackedSize, stride: chromaW },
                ],
            });
            staging.unmap();
            resolveReady(); // buffer now available for next GPU write
            onReady(frame);
        } catch (e) {
            // Ensure buffer is always released even if VideoFrame construction fails
            try { staging.unmap(); } catch { /* already unmapped or destroyed */ }
            resolveReady();
            warnLog?.log('startReadbackWithCallback: error in readback callback:', e);
        }
    }).catch((e: unknown) => {
        // mapAsync rejected (device lost, buffer destroyed, etc.)
        resolveReady(); // prevent deadlock
        warnLog?.log('startReadbackWithCallback: mapAsync failed:', e);
    });
}

/**
 * Await all in-flight readback operations. Use for clean shutdown.
 */
export async function awaitAllPendingReadbacks(): Promise<void> {
    await stagingReadyPromises[0];
    await stagingReadyPromises[1];
}

/**
 * Clean up all GPU resources held by the YUV converter.
 */
export function disposeYUVResources(): void {
    compositeTexture?.destroy();
    compositeTexture = null;
    compositeTextureView = null;
    compositeKey = '';

    yStorageBuf?.destroy();
    uStorageBuf?.destroy();
    vStorageBuf?.destroy();
    stagingBufs[0]?.destroy();
    stagingBufs[1]?.destroy();
    paramsBuf?.destroy();

    yStorageBuf = null;
    uStorageBuf = null;
    vStorageBuf = null;
    stagingBufs = [null, null];
    currentStagingIdx = 0;
    stagingReadyPromises = [Promise.resolve(), Promise.resolve()];
    paramsBuf = null;
    bufferKey = '';

    cachedYuvBindGroup = null;

    yPackedSize = 0;
    uvPackedSize = 0;

    yuvPipeline = null;
    device = null;
}

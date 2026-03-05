/**
 * WebGPU RGBA → I420 Converter
 *
 * Uses a WGSL compute shader to convert the composite RGBA/BGRA texture
 * (output of the blur pipeline) directly to I420 planar format on the GPU.
 * This avoids the slow CPU-side VideoFrame.copyTo({ format: 'I420' }) which
 * throws NotSupportedError on Android (RGBA) and Windows (BGRA).
 *
 * Architecture:
 *   GPU composite texture → compute shader → I420 staging buffers → mapAsync → VideoFrame(I420)
 *
 * Each compute thread handles a 2×2 luma block (4 Y + 1 U + 1 V).
 * Output uses u32-per-byte to avoid WGSL byte-level write limitations.
 */

import { Log } from 'logging';

const { infoLog, warnLog } = Log.get('VideoSegmentation');

// ── WGSL Compute Shader ────────────────────────────────────────────────────────

const RGBA_TO_I420_WGSL = /* wgsl */`
  @group(0) @binding(0) var src: texture_2d<f32>;
  @group(0) @binding(1) var<storage, read_write> yPlane: array<u32>;
  @group(0) @binding(2) var<storage, read_write> uPlane: array<u32>;
  @group(0) @binding(3) var<storage, read_write> vPlane: array<u32>;
  @group(0) @binding(4) var<uniform> params: vec4u; // width, height, chromaW, chromaH

  @compute @workgroup_size(16, 16)
  fn main(@builtin(global_invocation_id) gid: vec3u) {
    let chromaW = params.z;
    let chromaH = params.w;
    let width   = params.x;
    let height  = params.y;

    // Each thread handles one chroma position = one 2×2 luma block
    if (gid.x >= chromaW || gid.y >= chromaH) { return; }

    let baseX = gid.x * 2u;
    let baseY = gid.y * 2u;

    // Load 2×2 block with edge clamping
    let x0 = baseX;
    let y0 = baseY;
    let x1 = min(baseX + 1u, width - 1u);
    let y1 = min(baseY + 1u, height - 1u);

    // textureLoad normalizes to RGBA regardless of underlying format
    let c00 = textureLoad(src, vec2u(x0, y0), 0);
    let c10 = textureLoad(src, vec2u(x1, y0), 0);
    let c01 = textureLoad(src, vec2u(x0, y1), 0);
    let c11 = textureLoad(src, vec2u(x1, y1), 0);

    // BT.601 limited-range: Y=[16,235], UV=[16,240]
    let yOffset  = 16.0;
    let uvOffset = 128.0;

    // Compute Y for each pixel
    let y00 = clamp(round( 65.481 * c00.r + 128.553 * c00.g +  24.966 * c00.b + yOffset), 0.0, 255.0);
    let y10 = clamp(round( 65.481 * c10.r + 128.553 * c10.g +  24.966 * c10.b + yOffset), 0.0, 255.0);
    let y01 = clamp(round( 65.481 * c01.r + 128.553 * c01.g +  24.966 * c01.b + yOffset), 0.0, 255.0);
    let y11 = clamp(round( 65.481 * c11.r + 128.553 * c11.g +  24.966 * c11.b + yOffset), 0.0, 255.0);

    // Write Y values (one u32 per byte — no thread races)
    yPlane[y0 * width + x0] = u32(y00);
    yPlane[y0 * width + x1] = u32(y10);
    // Only write second row if it exists
    if (baseY + 1u < height) {
      yPlane[y1 * width + x0] = u32(y01);
      yPlane[y1 * width + x1] = u32(y11);
    }

    // Average 2×2 block for chroma subsampling
    let avg = (c00 + c10 + c01 + c11) * 0.25;

    let u = clamp(round(-37.797 * avg.r -  74.203 * avg.g + 112.0   * avg.b + uvOffset), 0.0, 255.0);
    let v = clamp(round(112.0   * avg.r -  93.786 * avg.g -  18.214 * avg.b + uvOffset), 0.0, 255.0);

    let chromaIdx = gid.y * chromaW + gid.x;
    uPlane[chromaIdx] = u32(u);
    vPlane[chromaIdx] = u32(v);
  }
`;

// ── Module State ────────────────────────────────────────────────────────────────

let device: GPUDevice | null = null;
let yuvPipeline: GPUComputePipeline | null = null;

// Composite texture (render target for blur → input for YUV compute)
let compositeTexture: GPUTexture | null = null;
let compositeTextureView: GPUTextureView | null = null;
let compositeKey = '';

// GPU storage + staging buffers, keyed by "width,height"
let bufferKey = '';
let yStorageBuf: GPUBuffer | null = null;
let uStorageBuf: GPUBuffer | null = null;
let vStorageBuf: GPUBuffer | null = null;
let yStagingBuf: GPUBuffer | null = null;
let uStagingBuf: GPUBuffer | null = null;
let vStagingBuf: GPUBuffer | null = null;
let paramsBuf: GPUBuffer | null = null;

// Pre-allocated I420 output buffer, reused across frames
let i420OutputBuf: ArrayBuffer | null = null;
let i420OutputSize = 0;

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

    infoLog?.log('WebGPU YUV converter initialized');
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

    // Destroy old buffers
    yStorageBuf?.destroy();
    uStorageBuf?.destroy();
    vStorageBuf?.destroy();
    yStagingBuf?.destroy();
    uStagingBuf?.destroy();
    vStagingBuf?.destroy();
    paramsBuf?.destroy();

    const ySize = width * height;
    const chromaW = Math.ceil(width / 2);
    const chromaH = Math.ceil(height / 2);
    const uvSize = chromaW * chromaH;

    // Storage buffers: u32 per element (one byte value stored per u32)
    yStorageBuf = device!.createBuffer({ size: ySize * 4, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
    uStorageBuf = device!.createBuffer({ size: uvSize * 4, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
    vStorageBuf = device!.createBuffer({ size: uvSize * 4, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });

    // Staging buffers for mapAsync readback
    yStagingBuf = device!.createBuffer({ size: ySize * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
    uStagingBuf = device!.createBuffer({ size: uvSize * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
    vStagingBuf = device!.createBuffer({ size: uvSize * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });

    // Uniform params buffer: vec4u (width, height, chromaW, chromaH)
    paramsBuf = device!.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    const paramsData = new Uint32Array([width, height, chromaW, chromaH]);
    device!.queue.writeBuffer(paramsBuf, 0, paramsData);

    // Pre-allocate I420 output buffer
    const totalI420 = ySize + uvSize * 2;
    if (i420OutputSize !== totalI420) {
        i420OutputBuf = new ArrayBuffer(totalI420);
        i420OutputSize = totalI420;
    }

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

    const chromaW = Math.ceil(width / 2);
    const chromaH = Math.ceil(height / 2);
    const ySize = width * height;
    const uvSize = chromaW * chromaH;

    // Compute pass
    const pass = encoder.beginComputePass();
    pass.setPipeline(yuvPipeline);
    pass.setBindGroup(0, device!.createBindGroup({
        layout: yuvPipeline.getBindGroupLayout(0),
        entries: [
            { binding: 0, resource: sourceTexture.createView() },
            { binding: 1, resource: { buffer: yStorageBuf! } },
            { binding: 2, resource: { buffer: uStorageBuf! } },
            { binding: 3, resource: { buffer: vStorageBuf! } },
            { binding: 4, resource: { buffer: paramsBuf! } },
        ],
    }));
    pass.dispatchWorkgroups(Math.ceil(chromaW / 16), Math.ceil(chromaH / 16));
    pass.end();

    // Copy storage → staging
    encoder.copyBufferToBuffer(yStorageBuf!, 0, yStagingBuf!, 0, ySize * 4);
    encoder.copyBufferToBuffer(uStorageBuf!, 0, uStagingBuf!, 0, uvSize * 4);
    encoder.copyBufferToBuffer(vStorageBuf!, 0, vStagingBuf!, 0, uvSize * 4);
}

/**
 * Map staging buffers, extract I420 bytes, and construct an I420 VideoFrame.
 * Must be called AFTER the command encoder containing encodeRGBAtoI420 has been submitted.
 */
export async function createI420VideoFrame(
    width: number,
    height: number,
    timestamp: number,
    duration?: number,
): Promise<VideoFrame> {
    const ySize = width * height;
    const chromaW = Math.ceil(width / 2);
    const chromaH = Math.ceil(height / 2);
    const uvSize = chromaW * chromaH;

    // Map all three staging buffers concurrently
    await Promise.all([
        yStagingBuf!.mapAsync(GPUMapMode.READ),
        uStagingBuf!.mapAsync(GPUMapMode.READ),
        vStagingBuf!.mapAsync(GPUMapMode.READ),
    ]);

    // Extract every 4th byte (u32 → u8) into the pre-allocated I420 buffer
    const out = new Uint8Array(i420OutputBuf!, 0, ySize + uvSize * 2);

    const yData = new Uint32Array(yStagingBuf!.getMappedRange());
    const uData = new Uint32Array(uStagingBuf!.getMappedRange());
    const vData = new Uint32Array(vStagingBuf!.getMappedRange());

    // Y plane
    for (let i = 0; i < ySize; i++) {
        out[i] = yData[i] & 0xFF;
    }
    // U plane
    const uOffset = ySize;
    for (let i = 0; i < uvSize; i++) {
        out[uOffset + i] = uData[i] & 0xFF;
    }
    // V plane
    const vOffset = ySize + uvSize;
    for (let i = 0; i < uvSize; i++) {
        out[vOffset + i] = vData[i] & 0xFF;
    }

    yStagingBuf!.unmap();
    uStagingBuf!.unmap();
    vStagingBuf!.unmap();

    // Construct I420 VideoFrame
    const yStride = width;
    const uvStride = chromaW;

    const frame = new VideoFrame(i420OutputBuf!, {
        format: 'I420',
        codedWidth: width,
        codedHeight: height,
        timestamp,
        duration,
        layout: [
            { offset: 0, stride: yStride },
            { offset: ySize, stride: uvStride },
            { offset: ySize + uvSize, stride: uvStride },
        ],
    });

    return frame;
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
    yStagingBuf?.destroy();
    uStagingBuf?.destroy();
    vStagingBuf?.destroy();
    paramsBuf?.destroy();

    yStorageBuf = null;
    uStorageBuf = null;
    vStorageBuf = null;
    yStagingBuf = null;
    uStagingBuf = null;
    vStagingBuf = null;
    paramsBuf = null;
    bufferKey = '';

    i420OutputBuf = null;
    i420OutputSize = 0;

    yuvPipeline = null;
    device = null;
}

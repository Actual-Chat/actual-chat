import { getLogs } from 'logging';

const { infoLog } = getLogs('VideoPipeline');

export interface DownscaleTarget {
    width: number;
    height: number;
    centerCrop?: boolean;
}

export interface DownscaleResult {
    frame: VideoFrame;
    target: DownscaleTarget;
}

interface TargetSlot {
    target: DownscaleTarget;
    canvas: OffscreenCanvas;
    ctx: GPUCanvasContext;
}

const SHADER_WGSL = `
struct Uniforms {
    // [sourceW, sourceH, targetW, targetH]
    srcDstDims: vec4<f32>,
    // rotation in {0,1,2,3} = {0, 90, 180, 270} CW, centerCrop in {0,1}
    rotationCrop: vec2<u32>,
};

@group(0) @binding(0) var linSampler: sampler;
@group(0) @binding(1) var srcTex: texture_external;
@group(0) @binding(2) var<uniform> u: Uniforms;

struct VsOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
};

// Fullscreen triangle: draw call uses 3 verts, covers [-1,1]^2 with UV [0,1]^2.
@vertex
fn vs(@builtin(vertex_index) vid: u32) -> VsOut {
    var out: VsOut;
    let x = f32((vid << 1u) & 2u);
    let y = f32(vid & 2u);
    out.pos = vec4<f32>(x * 2.0 - 1.0, 1.0 - y * 2.0, 0.0, 1.0);
    out.uv = vec2<f32>(x, y);
    return out;
}

@fragment
fn fs(in: VsOut) -> @location(0) vec4<f32> {
    let rot = u.rotationCrop.x;
    let doCrop = u.rotationCrop.y != 0u;

    // Source display dimensions after applying VideoFrame.rotation CW.
    // Rotation 90/270 swaps W/H when viewing the source as displayable.
    var dispW = u.srcDstDims.x;
    var dispH = u.srcDstDims.y;
    if (rot == 1u || rot == 3u) {
        dispW = u.srcDstDims.y;
        dispH = u.srcDstDims.x;
    }
    let dstW = u.srcDstDims.z;
    let dstH = u.srcDstDims.w;

    // UV in displayable source space; apply center-crop.
    var uv = in.uv;
    if (doCrop) {
        let srcAspect = dispW / dispH;
        let dstAspect = dstW / dstH;
        if (srcAspect > dstAspect) {
            // Source wider than target — crop horizontally.
            let scale = dstAspect / srcAspect;
            uv.x = 0.5 + (uv.x - 0.5) * scale;
        } else if (srcAspect < dstAspect) {
            let scale = srcAspect / dstAspect;
            uv.y = 0.5 + (uv.y - 0.5) * scale;
        }
    }

    // Rotate UV back into source texture space (inverse of display rotation).
    // Image coords: Y grows downward, so CW rotation maps (px, py) → (1-py, px).
    // Display(x,y) = Buffer(src); src = inverse-forward(x,y).
    var src: vec2<f32>;
    if (rot == 0u) {
        src = uv;
    } else if (rot == 1u) {
        // 90 CW: Display(x,y) = Buffer(y, 1-x)
        src = vec2<f32>(uv.y, 1.0 - uv.x);
    } else if (rot == 2u) {
        // 180: Display(x,y) = Buffer(1-x, 1-y)
        src = vec2<f32>(1.0 - uv.x, 1.0 - uv.y);
    } else {
        // 270 CW: Display(x,y) = Buffer(1-y, x)
        src = vec2<f32>(1.0 - uv.y, uv.x);
    }

    return textureSampleBaseClampToEdge(srcTex, linSampler, src);
}
`;

export class WebGpuDownscaler {
    private readonly device: GPUDevice;
    private readonly sampler: GPUSampler;
    private readonly format: GPUTextureFormat;
    private pipeline: GPURenderPipeline | null = null;
    private bindGroupLayout: GPUBindGroupLayout | null = null;
    private uniformBuffer: GPUBuffer | null = null;
    private slots: TargetSlot[] = [];
    private loggedFirstFrame = false;

    constructor(device: GPUDevice) {
        this.device = device;
        this.sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
        this.format = navigator.gpu.getPreferredCanvasFormat();
    }

    configure(targets: DownscaleTarget[]): void {
        if (targets.length === 0)
            throw new Error('WebGpuDownscaler.configure: at least one target required');

        this.disposeSlots();
        this.ensurePipeline();

        this.slots = targets.map(t => {
            const canvas = new OffscreenCanvas(t.width, t.height);
            const ctx = canvas.getContext('webgpu');
            if (!ctx)
                throw new Error('Failed to get webgpu context on OffscreenCanvas');
            ctx.configure({
                device: this.device,
                format: this.format,
                alphaMode: 'opaque',
            });
            return { target: { centerCrop: true, ...t }, canvas, ctx };
        });

        infoLog?.log(`WebGpuDownscaler configured with ${targets.length} target(s): ${
            targets.map(t => `${t.width}x${t.height}`).join(', ')}`);
    }

    process(source: VideoFrame, fallbackRotationDeg?: number): DownscaleResult[] {
        if (!this.pipeline || !this.uniformBuffer || this.slots.length === 0)
            throw new Error('WebGpuDownscaler.process: not configured');

        // Prefer display dims over coded dims. Chrome's getUserMedia with
        // `resizeMode: 'crop-and-scale'` returns VideoFrames whose codedWidth/Height
        // report the camera's native plane (e.g. 1920x1080) while the actual
        // plane0 buffer is scaled to displayWidth/Height (e.g. 1280x720). Using
        // coded dims makes `importExternalTexture` default cropSize exceed plane0
        // → WebGPU validation spam ("cropSize exceeds texture size"). When display
        // ≤ coded, trust display.
        const srcW = source.displayWidth > 0 ? source.displayWidth : source.codedWidth;
        const srcH = source.displayHeight > 0 ? source.displayHeight : source.codedHeight;
        // Prefer spec-populated rotation. Fall back to a main-thread supplied value
        // derived from `screen.orientation.angle` — needed on Safari iOS where MSTP
        // does not populate VideoFrame.rotation.
        const rawRot = source.rotation ?? fallbackRotationDeg ?? 0;
        const rotationDeg = rawRot % 360;
        const rotationIdx = ((rotationDeg + 360) % 360) / 90 >>> 0;

        if (!this.loggedFirstFrame) {
            this.loggedFirstFrame = true;
            infoLog?.log(
                `Downscaler first frame: rotation=${source.rotation ?? 'null'}`
                + ` fallback=${fallbackRotationDeg ?? 'none'}`
                + ` coded=${source.codedWidth}x${source.codedHeight}`
                + ` display=${source.displayWidth}x${source.displayHeight}`
                + ` targets=${this.slots.map(s => `${s.target.width}x${s.target.height}`).join(',')}`
                + ` rotationIdx=${rotationIdx}`,
            );
        }

        // Normalize visibleRect so Chrome's importExternalTexture sees a crop
        // that fits Plane0. With getUserMedia `resizeMode: 'crop-and-scale'`
        // source.codedWidth/Height may report the camera's pre-scale plane
        // (e.g. 1920x1080) while plane0 is actually scaled to display dims
        // (1280x720). Default cropSize = codedW/H → exceeds plane0 → validation
        // error spam + every frame skipped.
        let input = source;
        if (source.codedWidth > srcW || source.codedHeight > srcH) {
            try {
                input = new VideoFrame(source, {
                    visibleRect: { x: 0, y: 0, width: srcW, height: srcH },
                });
                source.close();
            }
            catch {
                input = source;
            }
        }
        const externalTex = this.device.importExternalTexture({ source: input });
        const timestamp = source.timestamp;
        const duration = source.duration ?? undefined;
        const results: DownscaleResult[] = [];

        for (const slot of this.slots) {
            const uniformData = new ArrayBuffer(32);
            const f32 = new Float32Array(uniformData, 0, 4);
            const u32 = new Uint32Array(uniformData, 16, 4);
            f32[0] = srcW; f32[1] = srcH;
            f32[2] = slot.target.width; f32[3] = slot.target.height;
            u32[0] = rotationIdx;
            u32[1] = slot.target.centerCrop === false ? 0 : 1;
            this.device.queue.writeBuffer(this.uniformBuffer, 0, uniformData);

            const bindGroup = this.device.createBindGroup({
                layout: this.bindGroupLayout!,
                entries: [
                    { binding: 0, resource: this.sampler },
                    { binding: 1, resource: externalTex },
                    { binding: 2, resource: { buffer: this.uniformBuffer } },
                ],
            });

            const encoder = this.device.createCommandEncoder();
            const view = slot.ctx.getCurrentTexture().createView();
            const pass = encoder.beginRenderPass({
                colorAttachments: [{
                    view,
                    loadOp: 'clear',
                    storeOp: 'store',
                    clearValue: { r: 0, g: 0, b: 0, a: 1 },
                }],
            });
            pass.setPipeline(this.pipeline);
            pass.setBindGroup(0, bindGroup);
            pass.draw(3);
            pass.end();
            this.device.queue.submit([encoder.finish()]);

            // new VideoFrame(OffscreenCanvas) implicitly syncs with prior submits.
            const outFrame = new VideoFrame(slot.canvas, { timestamp, duration });
            results.push({ frame: outFrame, target: slot.target });
        }

        input.close();
        return results;
    }

    dispose(): void {
        this.disposeSlots();
        this.uniformBuffer?.destroy();
        this.uniformBuffer = null;
        this.pipeline = null;
        this.bindGroupLayout = null;
    }

    private ensurePipeline(): void {
        if (this.pipeline) return;

        const module = this.device.createShaderModule({ code: SHADER_WGSL });
        this.bindGroupLayout = this.device.createBindGroupLayout({
            entries: [
                { binding: 0, visibility: GPUShaderStage.FRAGMENT, sampler: {} },
                { binding: 1, visibility: GPUShaderStage.FRAGMENT, externalTexture: {} },
                { binding: 2, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
            ],
        });
        const pipelineLayout = this.device.createPipelineLayout({
            bindGroupLayouts: [this.bindGroupLayout],
        });
        this.pipeline = this.device.createRenderPipeline({
            layout: pipelineLayout,
            vertex: { module, entryPoint: 'vs' },
            fragment: {
                module,
                entryPoint: 'fs',
                targets: [{ format: this.format }],
            },
            primitive: { topology: 'triangle-list' },
        });
        this.uniformBuffer = this.device.createBuffer({
            size: 32,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
    }

    private disposeSlots(): void {
        for (const slot of this.slots) {
            try { slot.ctx.unconfigure(); } catch { /* ignore */ }
        }
        this.slots = [];
    }
}


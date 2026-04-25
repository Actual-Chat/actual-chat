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
    key: string;
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

const UNIFORM_BYTES = 32;

function slotKey(t: DownscaleTarget): string {
    return `${t.width}x${t.height}:${t.centerCrop === false ? 0 : 1}`;
}

// Outputs are GPU-resident (Chrome canvas-backed VideoFrames). Do NOT insert
// `copyTo` / `getImageData` / `readPixels` between this class and the encoder
// path — every readback round-trips through CPU and bleeds battery on mobile.
export class WebGpuDownscaler {
    private readonly device: GPUDevice;
    private readonly sampler: GPUSampler;
    private readonly format: GPUTextureFormat;
    private readonly slotStride: number;
    private pipeline: GPURenderPipeline | null = null;
    private bindGroupLayout: GPUBindGroupLayout | null = null;
    private uniformBuffer: GPUBuffer | null = null;
    private uniformBufferCapacity = 0;
    private uniformStaging: Uint8Array | null = null;
    private slots: TargetSlot[] = [];
    private loggedFirstFrame = false;
    private loggedIdentitySkip = false;
    private loggedAllIdentitySkip = false;

    constructor(device: GPUDevice) {
        this.device = device;
        this.sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
        this.format = navigator.gpu.getPreferredCanvasFormat();
        // Dynamic-offset uniform regions must align to device limit (typically 256).
        const align = device.limits.minUniformBufferOffsetAlignment;
        this.slotStride = Math.max(align, UNIFORM_BYTES);
    }

    configure(targets: DownscaleTarget[]): void {
        if (targets.length === 0)
            throw new Error('WebGpuDownscaler.configure: at least one target required');

        this.ensurePipeline();
        this.ensureUniformCapacity(targets.length);

        // Multi-map of survivor slots keyed by dims+crop. Multiple targets with
        // identical dims each consume one slot from the pool.
        const survivors = new Map<string, TargetSlot[]>();
        for (const slot of this.slots) {
            const arr = survivors.get(slot.key);
            if (arr) arr.push(slot);
            else survivors.set(slot.key, [slot]);
        }

        const newSlots: TargetSlot[] = [];
        const kept = new Set<TargetSlot>();
        for (const t of targets) {
            const target: DownscaleTarget = { centerCrop: true, ...t };
            const key = slotKey(target);
            const pool = survivors.get(key);
            const existing = pool && pool.length > 0 ? pool.shift()! : undefined;
            if (existing) {
                existing.target = target;
                kept.add(existing);
                newSlots.push(existing);
                continue;
            }
            const canvas = new OffscreenCanvas(target.width, target.height);
            const ctx = canvas.getContext('webgpu');
            if (!ctx)
                throw new Error('Failed to get webgpu context on OffscreenCanvas');
            ctx.configure({
                device: this.device,
                format: this.format,
                alphaMode: 'opaque',
            });
            newSlots.push({ target, canvas, ctx, key });
        }

        let disposed = 0;
        for (const slot of this.slots) {
            if (kept.has(slot)) continue;
            try { slot.ctx.unconfigure(); } catch { /* ignore */ }
            disposed++;
        }
        this.slots = newSlots;

        infoLog?.log(
            `WebGpuDownscaler configured: ${targets.length} target(s) (${kept.size} reused, ${disposed} disposed)`
            + ` — ${targets.map(t => `${t.width}x${t.height}`).join(', ')}`,
        );
    }

    process(source: VideoFrame, fallbackRotationDeg?: number): DownscaleResult[] {
        if (!this.pipeline || !this.uniformBuffer || !this.bindGroupLayout || this.slots.length === 0)
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
        // Round (not truncate) to absorb sensor-fusion float noise:
        // 89.999° must map to idx 1, not 0; 359.999° must wrap to 0.
        const rawRot = source.rotation ?? fallbackRotationDeg ?? 0;
        const rotationDeg = ((rawRot % 360) + 360) % 360;
        const rotationIdx = Math.round(rotationDeg / 90) % 4;

        if (!this.loggedFirstFrame) {
            this.loggedFirstFrame = true;
            infoLog?.log(
                `Downscaler first frame: rotation=${source.rotation ?? 'null'}`
                + ` fallback=${fallbackRotationDeg ?? 'none'}`
                + ` coded=${source.codedWidth}x${source.codedHeight}`
                + ` display=${source.displayWidth}x${source.displayHeight}`
                + ` targets=${this.slots.map(s => `${s.target.width}x${s.target.height}`).join(',')}`
                + ` rotationIdx=${rotationIdx}`
                + ` slotStride=${this.slotStride}`,
            );
        }

        // Normalize visibleRect so Chrome's importExternalTexture sees a crop
        // that fits Plane0 (see comment above on coded/display divergence).
        // Capture timestamp/duration before any potential close — VideoFrame
        // attributes return 0/null after close().
        const timestamp = source.timestamp;
        const duration = source.duration ?? undefined;
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

        // Identity slot = output dims match source AND rotation is zero. We
        // skip the render entirely and clone the input frame; saves a render
        // pass and (when ALL slots are identity) the importExternalTexture
        // upload too — meaningful on Safari iOS where the import is not
        // free.
        const isIdentity = (slot: TargetSlot): boolean =>
            rotationIdx === 0
            && slot.target.width === srcW
            && slot.target.height === srcH;

        let renderingCount = 0;
        for (const slot of this.slots) {
            if (!isIdentity(slot)) renderingCount++;
        }

        if (renderingCount > 0) {
            const externalTex = this.device.importExternalTexture({ source: input });

            // Pack uniforms for all rendering slots into one staging buffer;
            // single writeBuffer call replaces N small writes.
            const totalBytes = renderingCount * this.slotStride;
            if (!this.uniformStaging || this.uniformStaging.byteLength < totalBytes)
                this.uniformStaging = new Uint8Array(totalBytes);
            const staging = this.uniformStaging.subarray(0, totalBytes);
            let writeIdx = 0;
            for (const slot of this.slots) {
                if (isIdentity(slot)) continue;
                const off = writeIdx * this.slotStride;
                const f32 = new Float32Array(staging.buffer, staging.byteOffset + off, 4);
                const u32 = new Uint32Array(staging.buffer, staging.byteOffset + off + 16, 4);
                f32[0] = srcW; f32[1] = srcH;
                f32[2] = slot.target.width; f32[3] = slot.target.height;
                u32[0] = rotationIdx;
                u32[1] = slot.target.centerCrop === false ? 0 : 1;
                writeIdx++;
            }
            this.device.queue.writeBuffer(
                this.uniformBuffer, 0, staging.buffer, staging.byteOffset, totalBytes,
            );

            // One bind group per frame (externalTex changes each frame; uniform
            // buffer is shared via dynamic offset).
            const bindGroup = this.device.createBindGroup({
                layout: this.bindGroupLayout,
                entries: [
                    { binding: 0, resource: this.sampler },
                    { binding: 1, resource: externalTex },
                    { binding: 2, resource: { buffer: this.uniformBuffer, offset: 0, size: UNIFORM_BYTES } },
                ],
            });

            // Single command encoder + single submit batches all render passes.
            const encoder = this.device.createCommandEncoder();
            let drawIdx = 0;
            for (const slot of this.slots) {
                if (isIdentity(slot)) continue;
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
                pass.setBindGroup(0, bindGroup, [drawIdx * this.slotStride]);
                pass.draw(3);
                pass.end();
                drawIdx++;
            }
            this.device.queue.submit([encoder.finish()]);
        }
        else if (!this.loggedAllIdentitySkip) {
            this.loggedAllIdentitySkip = true;
            infoLog?.log('Downscaler: all slots identity — importExternalTexture skipped');
        }

        // Build results in slot order. Identity slots clone the input frame
        // (refcount bump, no copy); rendered slots wrap the canvas.
        const results: DownscaleResult[] = [];
        let identityHits = 0;
        for (const slot of this.slots) {
            if (isIdentity(slot)) {
                results.push({ frame: input.clone(), target: slot.target });
                identityHits++;
            } else {
                // new VideoFrame(OffscreenCanvas) implicitly syncs with prior submits.
                const outFrame = new VideoFrame(slot.canvas, { timestamp, duration });
                results.push({ frame: outFrame, target: slot.target });
            }
        }

        if (identityHits > 0 && !this.loggedIdentitySkip) {
            this.loggedIdentitySkip = true;
            infoLog?.log(`Downscaler: ${identityHits}/${this.slots.length} slot(s) identity short-circuit`);
        }

        input.close();
        return results;
    }

    dispose(): void {
        this.disposeSlots();
        this.uniformBuffer?.destroy();
        this.uniformBuffer = null;
        this.uniformBufferCapacity = 0;
        this.uniformStaging = null;
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
                {
                    binding: 2,
                    visibility: GPUShaderStage.FRAGMENT,
                    buffer: { type: 'uniform', hasDynamicOffset: true, minBindingSize: UNIFORM_BYTES },
                },
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
    }

    private ensureUniformCapacity(slotCount: number): void {
        const neededBytes = Math.max(slotCount, 1) * this.slotStride;
        if (this.uniformBuffer && this.uniformBufferCapacity >= neededBytes) return;
        this.uniformBuffer?.destroy();
        this.uniformBuffer = this.device.createBuffer({
            size: neededBytes,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        this.uniformBufferCapacity = neededBytes;
    }

    private disposeSlots(): void {
        for (const slot of this.slots) {
            try { slot.ctx.unconfigure(); } catch { /* ignore */ }
        }
        this.slots = [];
    }
}

import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { WebGPUManager } from './webgpu-manager';

const { infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

// Per-frame `await device.queue.onSubmittedWorkDone()` is needed on iOS
// Safari to drain GPU work before constructing canvas-backed VideoFrames —
// each `new VideoFrame(canvas)` otherwise blocks the JS thread on an
// implicit per-canvas swap-chain sync. On Chromium-based browsers WebGPU
// pipelines submissions, so the per-frame drain only adds a round-trip
// without preventing a stall. Keeping it everywhere lets the GPU process
// queue grow unbounded under multi-encoder simulcast load (the Edge freeze
// regression). Drain only on iOS Safari; gate concurrent submissions with
// MAX_ACTIVE_SUBMISSIONS elsewhere so we still bound queue depth.
const NEEDS_PER_FRAME_DRAIN = DeviceInfo.isIos && DeviceInfo.isWebKit;
const MAX_ACTIVE_SUBMISSIONS = 2;

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
    // Intermediate storage texture for the compute path. Allocated only when
    // useStoragePath is active. Compute writes here; result is copied into
    // the canvas swap-chain via copyTextureToTexture (advances the swap chain
    // the way a render pass would).
    storageTex?: GPUTexture | null;
    storageView?: GPUTextureView | null;
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

    let srcAspect = dispW / dispH;
    let dstAspect = dstW / dstH;
    // Treat 1:1 as landscape for orientation match purposes (arbitrary, stable).
    let orientMatch = (srcAspect >= 1.0) == (dstAspect >= 1.0);

    // UV starts in dst-display space [0,1]^2.
    // - doCrop + orientMatch  → fill (zoom in to drop bars on minor-axis aspect mismatch).
    // - doCrop + !orientMatch → fit  (portrait↔landscape never zooms; letterbox instead).
    // - !doCrop               → stretch (uv unchanged).
    var uv = in.uv;
    var letterbox = false;
    if (doCrop && orientMatch) {
        if (srcAspect > dstAspect) {
            // Source wider than target — crop horizontally.
            let scale = dstAspect / srcAspect;
            uv.x = 0.5 + (uv.x - 0.5) * scale;
        } else if (srcAspect < dstAspect) {
            let scale = srcAspect / dstAspect;
            uv.y = 0.5 + (uv.y - 0.5) * scale;
        }
    } else if (doCrop) {
        // Orientation mismatch — fit (contain). Expand uv past [0,1] on the
        // axis where the source is smaller than dst; out-of-range uv samples
        // become black bars below.
        if (srcAspect > dstAspect) {
            let scale = srcAspect / dstAspect;
            uv.y = 0.5 + (uv.y - 0.5) * scale;
        } else if (srcAspect < dstAspect) {
            let scale = dstAspect / srcAspect;
            uv.x = 0.5 + (uv.x - 0.5) * scale;
        }
        letterbox = true;
    }

    // Bar pixels — output black before sampling (avoid clamp-to-edge smear).
    if (letterbox && (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)) {
        return vec4<f32>(0.0, 0.0, 0.0, 1.0);
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

// Compute-path WGSL: single dispatch writes up to 3 differently-sized layers
// into canvas swap-chain storage textures. Replaces the 3-render-pass loop on
// devices where the canvas format supports STORAGE_BINDING (bgra8unorm-storage
// feature on Chromium/Safari, or core rgba8unorm). The format placeholder is
// substituted at module-build time so the same shader source covers both.
//
// Uniforms layout (std140-compatible, all members 16-byte aligned):
//   layers: array<LayerU, 3>  // 3 × 32 = 96 bytes
//   cfg:    vec4<u32>          // 16 bytes
// Total: 112 bytes (round up to 16 for buffer size).
//
// LayerU:
//   dims:  vec4<f32>  [srcW, srcH, dstW, dstH]
//   flags: vec4<u32>  [rotation 0..3, doCrop 0/1, enabled 0/1, _pad]
//
// cfg:
//   [maxDstW, maxDstH, layerCount, _pad]  — used to bound the dispatch and skip layers.
//   (`meta` is a reserved keyword in WGSL — using `cfg` instead.)
const COMPUTE_WGSL = (fmt: string) => `
struct LayerU {
    dims: vec4<f32>,
    flags: vec4<u32>,
};
struct Uniforms {
    layers: array<LayerU, 3>,
    cfg: vec4<u32>,
};
@group(0) @binding(0) var linSampler: sampler;
@group(0) @binding(1) var srcTex: texture_external;
@group(0) @binding(2) var<uniform> u: Uniforms;
@group(0) @binding(3) var out0: texture_storage_2d<${fmt}, write>;
@group(0) @binding(4) var out1: texture_storage_2d<${fmt}, write>;
@group(0) @binding(5) var out2: texture_storage_2d<${fmt}, write>;

fn sampleLayer(L: LayerU, gid: vec2<u32>) -> vec4<f32> {
    let rot = L.flags.x;
    let doCrop = L.flags.y != 0u;

    var dispW = L.dims.x;
    var dispH = L.dims.y;
    if (rot == 1u || rot == 3u) {
        dispW = L.dims.y;
        dispH = L.dims.x;
    }
    let dstW = L.dims.z;
    let dstH = L.dims.w;
    let srcAspect = dispW / dispH;
    let dstAspect = dstW / dstH;
    let orientMatch = (srcAspect >= 1.0) == (dstAspect >= 1.0);

    var uv = vec2<f32>(
        (f32(gid.x) + 0.5) / dstW,
        (f32(gid.y) + 0.5) / dstH,
    );
    var letterbox = false;
    if (doCrop && orientMatch) {
        if (srcAspect > dstAspect) {
            uv.x = 0.5 + (uv.x - 0.5) * (dstAspect / srcAspect);
        } else if (srcAspect < dstAspect) {
            uv.y = 0.5 + (uv.y - 0.5) * (srcAspect / dstAspect);
        }
    } else if (doCrop) {
        if (srcAspect > dstAspect) {
            uv.y = 0.5 + (uv.y - 0.5) * (srcAspect / dstAspect);
        } else if (srcAspect < dstAspect) {
            uv.x = 0.5 + (uv.x - 0.5) * (dstAspect / srcAspect);
        }
        letterbox = true;
    }
    if (letterbox && (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)) {
        return vec4<f32>(0.0, 0.0, 0.0, 1.0);
    }

    var src: vec2<f32>;
    if (rot == 0u) { src = uv; }
    else if (rot == 1u) { src = vec2<f32>(uv.y, 1.0 - uv.x); }
    else if (rot == 2u) { src = vec2<f32>(1.0 - uv.x, 1.0 - uv.y); }
    else               { src = vec2<f32>(1.0 - uv.y, uv.x); }
    return textureSampleBaseClampToEdge(srcTex, linSampler, src);
}

@compute @workgroup_size(8, 8, 1)
fn cs(@builtin(global_invocation_id) gid: vec3<u32>) {
    if (gid.x >= u.cfg.x || gid.y >= u.cfg.y) { return; }
    let n = u.cfg.z;
    if (n > 0u && u.layers[0].flags.z == 1u) {
        let dW = u32(u.layers[0].dims.z);
        let dH = u32(u.layers[0].dims.w);
        if (gid.x < dW && gid.y < dH) {
            textureStore(out0, vec2<i32>(gid.xy), sampleLayer(u.layers[0], gid.xy));
        }
    }
    if (n > 1u && u.layers[1].flags.z == 1u) {
        let dW = u32(u.layers[1].dims.z);
        let dH = u32(u.layers[1].dims.w);
        if (gid.x < dW && gid.y < dH) {
            textureStore(out1, vec2<i32>(gid.xy), sampleLayer(u.layers[1], gid.xy));
        }
    }
    if (n > 2u && u.layers[2].flags.z == 1u) {
        let dW = u32(u.layers[2].dims.z);
        let dH = u32(u.layers[2].dims.w);
        if (gid.x < dW && gid.y < dH) {
            textureStore(out2, vec2<i32>(gid.xy), sampleLayer(u.layers[2], gid.xy));
        }
    }
}
`;

const COMPUTE_UNIFORM_BYTES = 16 * 8; // 3 LayerU (3*32=96) + meta (16) = 112, round up to 128
const MAX_COMPUTE_LAYERS = 3;

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
    private activeSubmissions = 0;
    private pendingSubmissionDrains: Promise<void>[] = [];
    private deferredSlotDisposals: TargetSlot[] = [];
    private deferredBufferDisposals: GPUBuffer[] = [];
    private loggedFirstFrame = false;
    private loggedIdentitySkip = false;
    private loggedAllIdentitySkip = false;
    private loggedRewrapFail = false;
    private loggedCanvasNormalize = false;
    // Reused for the canvas-normalize fallback when visibleRect re-wrap can't
    // bring `coded` down to match Plane0 (e.g. Chrome MSTP frames whose buffer
    // is internally scaled below codedWidth/Height). One allocation per dim.
    private normalizeCanvas: OffscreenCanvas | null = null;
    private normalizeCtx: OffscreenCanvasRenderingContext2D | null = null;
    // Set when device.lost fires or when a submit/onSubmittedWorkDone throws.
    // Once invalid the downscaler refuses further process() calls — every later
    // frame would otherwise re-throw the same OperationError and produce the
    // per-frame "external Instance reference no longer exists" log storm we
    // saw on Edge under multi-encoder simulcast load.
    private invalid = false;
    private lostListenerDispose: (() => void) | null = null;
    private firstErrorLogged = false;
    // Counts how many times the soft submission cap forced us to await a
    // prior submission before the current frame could submit. Sustained
    // non-zero values mean the GPU process is the bottleneck — i.e. we're
    // already on the path that, unmitigated, leads to the device-lost
    // freeze. Surfaced through the sender pipeline-counters log (D4) so
    // freeze post-mortems show whether GPU pressure was rising before
    // the cascade. Reset by getCapHitsAndReset().
    private capHits = 0;

    // Compute-path (single-dispatch multi-storage-texture) state. Active when
    // the canvas format is storage-bindable (rgba8unorm core; bgra8unorm via
    // the `bgra8unorm-storage` feature). Falls back to the render-pass path
    // when the shader/pipeline fails async validation at init time. See
    // COMPUTE_WGSL for the shader.
    private useStoragePath = false;
    private computePipelineFailed = false;
    private computePipelinePromise: Promise<void> | null = null;
    private computePipeline: GPUComputePipeline | null = null;
    private computeBgLayout: GPUBindGroupLayout | null = null;
    private computeUniformBuffer: GPUBuffer | null = null;
    private computeUniformStaging: ArrayBuffer | null = null;
    // Three separate 1×1 dummies — WebGPU forbids aliasing the same texture
    // across multiple write-only storage entries in one bind group, even if
    // the shader gates writes via uniforms and never actually executes them.
    private dummyStorageTexs: (GPUTexture | null)[] = [null, null, null];
    private dummyStorageViews: (GPUTextureView | null)[] = [null, null, null];

    constructor(device: GPUDevice) {
        this.device = device;
        this.sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
        this.format = navigator.gpu.getPreferredCanvasFormat();
        // Dynamic-offset uniform regions must align to device limit (typically 256).
        const align = device.limits.minUniformBufferOffsetAlignment;
        this.slotStride = Math.max(align, UNIFORM_BYTES);
        // Compute-path eligibility: rgba8unorm is storage-bindable in core
        // WebGPU; bgra8unorm requires the `bgra8unorm-storage` feature opted
        // into at device init (see WebGPUManager.init).
        if (this.format === 'rgba8unorm') {
            this.useStoragePath = true;
        } else if (this.format === 'bgra8unorm' && WebGPUManager.hasFeature('bgra8unorm-storage')) {
            this.useStoragePath = true;
        }
        this.lostListenerDispose = WebGPUManager.addLostListener(() => {
            if (this.invalid) return;
            this.invalid = true;
            warnLog?.log('Downscaler invalidated by device.lost');
        });
    }

    get isInvalid(): boolean { return this.invalid; }

    getCapHitsAndReset(): number {
        const v = this.capHits;
        this.capHits = 0;
        return v;
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
            // RENDER_ATTACHMENT for the render-path; COPY_DST so the
            // compute-path can copyTextureToTexture from its intermediate
            // storage texture into the swap-chain. We don't use STORAGE_BINDING
            // on the canvas itself because Chrome's canvas presenter doesn't
            // reliably advance the swap chain on STORAGE-only writes — produces
            // visible-black output despite the texture having content.
            const usage = GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_DST;
            ctx.configure({
                device: this.device,
                format: this.format,
                alphaMode: 'opaque',
                usage,
            });
            const slot: TargetSlot = { target, canvas, ctx, key };
            // Allocate intermediate storage texture for the compute path.
            // Sized to the slot's target dims; compute writes here, then we
            // copyTextureToTexture into the canvas swap-chain.
            if (this.useStoragePath) {
                slot.storageTex = this.device.createTexture({
                    size: [target.width, target.height],
                    format: this.format,
                    usage: GPUTextureUsage.STORAGE_BINDING | GPUTextureUsage.COPY_SRC,
                });
                slot.storageView = slot.storageTex.createView();
            }
            newSlots.push(slot);
        }

        let disposed = 0;
        for (const slot of this.slots) {
            if (kept.has(slot)) continue;
            this.disposeSlot(slot);
            disposed++;
        }
        this.slots = newSlots;

        infoLog?.log(
            `WebGpuDownscaler configured: ${targets.length} target(s) (${kept.size} reused, ${disposed} disposed)`
            + ` — ${targets.map(t => `${t.width}x${t.height}`).join(', ')}`,
        );
    }

    async process(source: VideoFrame, opts?: {
        fallbackRotationDeg?: number;
    }): Promise<DownscaleResult[]> {
        if (this.invalid) {
            try { source.close(); } catch { /* already closed */ }
            throw new Error('WebGpuDownscaler.process: invalid (device lost or submit failed)');
        }
        if (!this.pipeline || !this.uniformBuffer || !this.bindGroupLayout || this.slots.length === 0)
            throw new Error('WebGpuDownscaler.process: not configured');
        const slots = this.slots.slice();
        const pipeline = this.pipeline;
        const uniformBuffer = this.uniformBuffer;
        const bindGroupLayout = this.bindGroupLayout;

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
        const rawRot = source.rotation ?? opts?.fallbackRotationDeg ?? 0;
        const rotationDeg = ((rawRot % 360) + 360) % 360;
        const rotationIdx = Math.round(rotationDeg / 90) % 4;

        if (!this.loggedFirstFrame) {
            this.loggedFirstFrame = true;
            infoLog?.log(
                `Downscaler first frame: rotation=${source.rotation ?? 'null'}`
                + ` fallback=${opts?.fallbackRotationDeg ?? 'none'}`
                + ` coded=${source.codedWidth}x${source.codedHeight}`
                + ` display=${source.displayWidth}x${source.displayHeight}`
                + ` targets=${slots.map(s => `${s.target.width}x${s.target.height}`).join(',')}`
                + ` rotationIdx=${rotationIdx}`
                + ` slotStride=${this.slotStride}`
                + ` path=${this.useStoragePath ? 'compute-storage' : 'render-pass'}`
                + ` gpuSync=${NEEDS_PER_FRAME_DRAIN ? 'per-frame-drain' : `cap-at-${MAX_ACTIVE_SUBMISSIONS}`}`,
            );
        }

        // Normalize the source so importExternalTexture's cropSize default
        // (= source.visibleRect) fits the actual Plane0 buffer. Chrome's MSTP
        // can deliver frames where codedWidth/Height > Plane0 buffer dim
        // (resizeMode='crop-and-scale' surface, or sensor-side scaling that
        // bypasses our resizeMode='none' constraint). Two-step normalization:
        //   1. Try visibleRect re-wrap (zero-copy, just tags a new visibleRect).
        //   2. On failure (or no improvement on re-wrap), draw the source onto
        //      an OffscreenCanvas at (srcW, srcH) and wrap the canvas as a
        //      fresh VideoFrame — guaranteed coded == Plane0.
        // Capture timestamp/duration before any potential close — VideoFrame
        // attributes return 0/null after close().
        const timestamp = source.timestamp;
        const duration = source.duration ?? undefined;
        let input = source;
        if (source.codedWidth > srcW || source.codedHeight > srcH) {
            let rewrapped: VideoFrame | null = null;
            try {
                rewrapped = new VideoFrame(source, {
                    visibleRect: { x: 0, y: 0, width: srcW, height: srcH },
                    timestamp,
                    duration,
                });
            }
            catch (e) {
                if (!this.loggedRewrapFail) {
                    this.loggedRewrapFail = true;
                    warnLog?.log(
                        `Downscaler: visibleRect re-wrap failed (coded=${source.codedWidth}x${source.codedHeight},`
                        + ` display=${srcW}x${srcH}):`, e);
                }
            }
            if (rewrapped) {
                source.close();
                input = rewrapped;
            } else {
                // Canvas-normalize fallback: draw source onto a (srcW, srcH)
                // offscreen canvas; the canvas-backed VideoFrame is guaranteed
                // to have coded == Plane0. Per-frame 2D copy — only fires when
                // re-wrap is unavailable (rare on modern Chrome).
                if (this.normalizeCanvas?.width !== srcW
                    || this.normalizeCanvas.height !== srcH) {
                    this.normalizeCanvas = new OffscreenCanvas(srcW, srcH);
                    this.normalizeCtx = this.normalizeCanvas.getContext('2d');
                }
                if (this.normalizeCtx) {
                    if (!this.loggedCanvasNormalize) {
                        this.loggedCanvasNormalize = true;
                        infoLog?.log(
                            `Downscaler: canvas-normalize fallback engaged (coded=${source.codedWidth}x${source.codedHeight}, display=${srcW}x${srcH})`);
                    }
                    this.normalizeCtx.drawImage(source as unknown as CanvasImageSource, 0, 0, srcW, srcH);
                    source.close();
                    input = new VideoFrame(this.normalizeCanvas, { timestamp, duration });
                } else {
                    input = source;
                }
            }
        }

        // Identity slot = output dims match source AND rotation is zero AND
        // there's no coded/display divergence. We skip the render entirely and
        // clone the input frame; saves a render pass and (when ALL slots are
        // identity) the importExternalTexture upload too — meaningful on
        // Safari iOS where the import is not free.
        //
        // Coded/display divergence guard: Chrome's MSTP can deliver frames
        // whose codedWidth/Height report the camera's native sensor dim
        // (e.g. 1920x1080) while the actual plane0 buffer is internally scaled
        // to displayWidth/Height (e.g. 1280x720 / 640x360). `clone()` preserves
        // codedWidth — the downstream encoder dim-mismatch guard then drops
        // every frame because frame.codedWidth (1920) != encoder.config.width
        // (640). Force a render in that case so the result is a fresh
        // canvas-backed VideoFrame whose codedWidth matches its plane0.
        const codedMatchesDisplay =
            source.codedWidth === srcW && source.codedHeight === srcH;
        const isIdentity = (slot: TargetSlot): boolean =>
            rotationIdx === 0
            && codedMatchesDisplay
            && slot.target.width === srcW
            && slot.target.height === srcH;

        let renderingCount = 0;
        for (const slot of slots) {
            if (!isIdentity(slot)) renderingCount++;
        }

        if (renderingCount > 0) {
            // Lazy-init compute pipeline on first frame. Validates via error
            // scope; on failure flips to render path forever for this instance.
            if (this.useStoragePath && !this.computePipeline && !this.computePipelineFailed)
                await this.ensureComputePipelineLazy();
            if (this.useStoragePath && this.computePipeline && this.computeBgLayout
                && this.computeUniformBuffer && this.computeUniformStaging
                && this.dummyStorageViews[0] && this.dummyStorageViews[1]
                && this.dummyStorageViews[2]) {
                await this.runComputePath(slots, isIdentity, input, srcW, srcH, rotationIdx);
            } else {
                await this.runRenderPath(slots, isIdentity, input, srcW, srcH, rotationIdx,
                    pipeline, uniformBuffer, bindGroupLayout);
            }
        }
        else if (!this.loggedAllIdentitySkip) {
            this.loggedAllIdentitySkip = true;
            infoLog?.log('Downscaler: all slots identity — importExternalTexture skipped');
        }

        // Build results in slot order. Identity slots clone the input frame
        // (refcount bump, no copy); rendered slots wrap the canvas. Timestamps
        // are inherited from the source frame — rebase to the recording-anchor
        // timeline happens at chunk emission, not per VideoFrame.
        const results: DownscaleResult[] = [];
        let identityHits = 0;
        for (const slot of slots) {
            if (isIdentity(slot)) {
                results.push({ frame: input.clone(), target: slot.target });
                identityHits++;
            } else {
                const outFrame = new VideoFrame(slot.canvas, { timestamp, duration });
                results.push({ frame: outFrame, target: slot.target });
            }
        }

        if (identityHits > 0 && !this.loggedIdentitySkip) {
            this.loggedIdentitySkip = true;
            infoLog?.log(`Downscaler: ${identityHits}/${slots.length} slot(s) identity short-circuit`);
        }

        input.close();
        return results;
    }

    dispose(): void {
        if (this.lostListenerDispose) {
            this.lostListenerDispose();
            this.lostListenerDispose = null;
        }
        this.disposeSlots();
        if (this.uniformBuffer)
            this.disposeBuffer(this.uniformBuffer);
        this.uniformBuffer = null;
        this.uniformBufferCapacity = 0;
        this.uniformStaging = null;
        this.pipeline = null;
        this.bindGroupLayout = null;
        if (this.computeUniformBuffer)
            this.disposeBuffer(this.computeUniformBuffer);
        this.computeUniformBuffer = null;
        this.computeUniformStaging = null;
        for (let i = 0; i < this.dummyStorageTexs.length; i++) {
            const d = this.dummyStorageTexs[i];
            if (d) try { d.destroy(); } catch { /* ignore */ }
            this.dummyStorageTexs[i] = null;
            this.dummyStorageViews[i] = null;
        }
        this.computePipeline = null;
        this.computeBgLayout = null;
        this.invalid = true;
    }

    private async runRenderPath(
        slots: TargetSlot[],
        isIdentity: (slot: TargetSlot) => boolean,
        input: VideoFrame,
        srcW: number,
        srcH: number,
        rotationIdx: number,
        pipeline: GPURenderPipeline,
        uniformBuffer: GPUBuffer,
        bindGroupLayout: GPUBindGroupLayout,
    ): Promise<void> {
        const externalTex = this.device.importExternalTexture({ source: input });

        // Pack uniforms for all rendering slots into one staging buffer;
        // single writeBuffer call replaces N small writes.
        let renderingCount = 0;
        for (const slot of slots) if (!isIdentity(slot)) renderingCount++;
        const totalBytes = renderingCount * this.slotStride;
        if (!this.uniformStaging || this.uniformStaging.byteLength < totalBytes)
            this.uniformStaging = new Uint8Array(totalBytes);
        const staging = this.uniformStaging.subarray(0, totalBytes);
        let writeIdx = 0;
        for (const slot of slots) {
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
            uniformBuffer, 0, staging.buffer, staging.byteOffset, totalBytes,
        );

        // One bind group per frame (externalTex changes each frame; uniform
        // buffer is shared via dynamic offset).
        const bindGroup = this.device.createBindGroup({
            layout: bindGroupLayout,
            entries: [
                { binding: 0, resource: this.sampler },
                { binding: 1, resource: externalTex },
                { binding: 2, resource: { buffer: uniformBuffer, offset: 0, size: UNIFORM_BYTES } },
            ],
        });

        // Single command encoder + single submit batches all render passes.
        const encoder = this.device.createCommandEncoder();
        let drawIdx = 0;
        for (const slot of slots) {
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
            pass.setPipeline(pipeline);
            pass.setBindGroup(0, bindGroup, [drawIdx * this.slotStride]);
            pass.draw(3);
            pass.end();
            drawIdx++;
        }
        try {
            if (!NEEDS_PER_FRAME_DRAIN)
                await this.waitForSubmissionSlot();
            this.device.queue.submit([encoder.finish()]);
            const drain = this.trackSubmissionDrain(slots.length);
            if (NEEDS_PER_FRAME_DRAIN)
                await drain;
        } catch (e) {
            this.invalidateFromGpuError(e, slots.length);
            this.flushDeferredDisposals();
            throw e;
        }
    }

    private async runComputePath(
        slots: TargetSlot[],
        isIdentity: (slot: TargetSlot) => boolean,
        input: VideoFrame,
        srcW: number,
        srcH: number,
        rotationIdx: number,
    ): Promise<void> {
        const computePipeline = this.computePipeline!;
        const computeBgLayout = this.computeBgLayout!;
        const computeUniformBuffer = this.computeUniformBuffer!;
        const stagingBuf = this.computeUniformStaging!;
        const dummyViews = this.dummyStorageViews;

        // Pack uniforms: 3 LayerU entries (unused = enabled:0) + meta vec4.
        // Layer N occupies bytes [N*32, N*32+32). meta sits at byte 96.
        // Uniform layout (4-byte words):
        //   layer 0: f32[0..3]=dims, u32[4..7]=flags  (words 0..7)
        //   layer 1: words 8..15
        //   layer 2: words 16..23
        //   meta:    words 24..27
        const f32 = new Float32Array(stagingBuf);
        const u32 = new Uint32Array(stagingBuf);
        for (let w = 0; w < f32.length; w++) f32[w] = 0;

        let maxW = 0, maxH = 0;
        let layerIdx = 0;
        const slotByLayer: number[] = [-1, -1, -1];
        for (let i = 0; i < slots.length; i++) {
            const slot = slots[i];
            if (isIdentity(slot)) continue;
            if (layerIdx >= MAX_COMPUTE_LAYERS) break;
            const off = layerIdx * 8;
            f32[off + 0] = srcW;
            f32[off + 1] = srcH;
            f32[off + 2] = slot.target.width;
            f32[off + 3] = slot.target.height;
            u32[off + 4] = rotationIdx;
            u32[off + 5] = slot.target.centerCrop === false ? 0 : 1;
            u32[off + 6] = 1; // enabled
            // u32[off + 7] = 0 (pad — zeroed above)
            if (slot.target.width > maxW) maxW = slot.target.width;
            if (slot.target.height > maxH) maxH = slot.target.height;
            slotByLayer[layerIdx] = i;
            layerIdx++;
        }
        u32[24] = maxW;
        u32[25] = maxH;
        u32[26] = layerIdx;
        // u32[27] = 0 (pad)
        this.device.queue.writeBuffer(computeUniformBuffer, 0, stagingBuf, 0, COMPUTE_UNIFORM_BYTES);

        const externalTex = this.device.importExternalTexture({ source: input });
        // Each binding slot gets a DISTINCT texture view, even when unused —
        // WebGPU validation rejects two write-only storage entries aliasing
        // the same texture. Hence per-slot dummy textures.
        const views: GPUTextureView[] = [
            dummyViews[0]!, dummyViews[1]!, dummyViews[2]!,
        ];
        for (let n = 0; n < layerIdx; n++) {
            const slot = slots[slotByLayer[n]];
            // storageView is allocated in configure() when useStoragePath is on.
            // If somehow missing (e.g. configure was called before the path was
            // armed), bail to render path by demoting.
            if (!slot.storageView) {
                this.markComputeFailed('compute path slot missing storageView');
                throw new Error('runComputePath: missing storageView');
            }
            views[n] = slot.storageView;
        }
        const bindGroup = this.device.createBindGroup({
            layout: computeBgLayout,
            entries: [
                { binding: 0, resource: this.sampler },
                { binding: 1, resource: externalTex },
                { binding: 2, resource: { buffer: computeUniformBuffer } },
                { binding: 3, resource: views[0] },
                { binding: 4, resource: views[1] },
                { binding: 5, resource: views[2] },
            ],
        });

        const encoder = this.device.createCommandEncoder();
        const pass = encoder.beginComputePass();
        pass.setPipeline(computePipeline);
        pass.setBindGroup(0, bindGroup);
        pass.dispatchWorkgroups(Math.ceil(maxW / 8), Math.ceil(maxH / 8), 1);
        pass.end();

        // Copy each intermediate storage texture into its canvas swap-chain.
        // Cheaper than a render pass, and properly marks the canvas as
        // "presented" so the new VideoFrame(canvas) sees fresh content.
        for (let n = 0; n < layerIdx; n++) {
            const slot = slots[slotByLayer[n]];
            const dstTex = slot.ctx.getCurrentTexture();
            encoder.copyTextureToTexture(
                { texture: slot.storageTex! },
                { texture: dstTex },
                [slot.target.width, slot.target.height, 1],
            );
        }

        try {
            if (!NEEDS_PER_FRAME_DRAIN)
                await this.waitForSubmissionSlot();
            this.device.queue.submit([encoder.finish()]);
            const drain = this.trackSubmissionDrain(slots.length);
            if (NEEDS_PER_FRAME_DRAIN)
                await drain;
        } catch (e) {
            this.invalidateFromGpuError(e, slots.length);
            this.flushDeferredDisposals();
            throw e;
        }
    }

    private async waitForSubmissionSlot(): Promise<void> {
        while (this.pendingSubmissionDrains.length >= MAX_ACTIVE_SUBMISSIONS) {
            this.capHits++;
            await this.pendingSubmissionDrains[0];
        }
    }

    private trackSubmissionDrain(slotsLength: number): Promise<void> {
        this.activeSubmissions++;
        const drain = this.device.queue.onSubmittedWorkDone()
            .catch((e: unknown) => {
                this.invalidateFromGpuError(e, slotsLength);
                throw e;
            })
            .finally(() => {
                this.activeSubmissions--;
                const index = this.pendingSubmissionDrains.indexOf(drain);
                if (index >= 0)
                    void this.pendingSubmissionDrains.splice(index, 1);
                this.flushDeferredDisposals();
            });
        this.pendingSubmissionDrains.push(drain);
        // Non-Safari does not await the drain on the current frame, so attach a
        // handler to keep asynchronous device-loss rejections observed.
        void drain.catch(() => { /* observed by invalidateFromGpuError */ });
        return drain;
    }

    private invalidateFromGpuError(error: unknown, slotsLength: number): void {
        this.invalid = true;
        if (this.firstErrorLogged)
            return;
        this.firstErrorLogged = true;
        const err = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
        errorLog?.log(
            `Downscaler GPU submit failed (invalidating): ${err} `
            + `activeSubmissions=${this.activeSubmissions} slots=${slotsLength}`);
    }

    private ensurePipeline(): void {
        if (!this.pipeline) {
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

    }

    // Lazy + validated compute-pipeline init. First call wraps creation in a
    // validation error scope and awaits the result; on validation error
    // (e.g. storage-format/feature mismatch on this device), `computePipelineFailed`
    // sticks and process() falls through to the render path. Subsequent calls
    // are a no-op once the pipeline is good or known-bad.
    private ensureComputePipelineLazy(): Promise<void> {
        if (this.computePipeline || this.computePipelineFailed)
            return Promise.resolve();
        if (!this.useStoragePath)
            return Promise.resolve();
        if (this.computePipelinePromise)
            return this.computePipelinePromise;

        this.computePipelinePromise = (async () => {
            this.device.pushErrorScope('validation');
            let createdShader: GPUShaderModule | null = null;
            let createdLayout: GPUBindGroupLayout | null = null;
            let createdPipeline: GPUComputePipeline | null = null;
            let createdBuffer: GPUBuffer | null = null;
            const createdDummies: (GPUTexture | null)[] = [null, null, null];
            try {
                createdShader = this.device.createShaderModule({ code: COMPUTE_WGSL(this.format) });
                createdLayout = this.device.createBindGroupLayout({
                    entries: [
                        { binding: 0, visibility: GPUShaderStage.COMPUTE, sampler: {} },
                        { binding: 1, visibility: GPUShaderStage.COMPUTE, externalTexture: {} },
                        {
                            binding: 2,
                            visibility: GPUShaderStage.COMPUTE,
                            buffer: { type: 'uniform', minBindingSize: COMPUTE_UNIFORM_BYTES },
                        },
                        {
                            binding: 3,
                            visibility: GPUShaderStage.COMPUTE,
                            storageTexture: { access: 'write-only', format: this.format, viewDimension: '2d' },
                        },
                        {
                            binding: 4,
                            visibility: GPUShaderStage.COMPUTE,
                            storageTexture: { access: 'write-only', format: this.format, viewDimension: '2d' },
                        },
                        {
                            binding: 5,
                            visibility: GPUShaderStage.COMPUTE,
                            storageTexture: { access: 'write-only', format: this.format, viewDimension: '2d' },
                        },
                    ],
                });
                const computePipelineLayout = this.device.createPipelineLayout({
                    bindGroupLayouts: [createdLayout],
                });
                createdPipeline = this.device.createComputePipeline({
                    layout: computePipelineLayout,
                    compute: { module: createdShader, entryPoint: 'cs' },
                });
                createdBuffer = this.device.createBuffer({
                    size: COMPUTE_UNIFORM_BYTES,
                    usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
                });
                for (let i = 0; i < MAX_COMPUTE_LAYERS; i++) {
                    createdDummies[i] = this.device.createTexture({
                        size: [1, 1],
                        format: this.format,
                        usage: GPUTextureUsage.STORAGE_BINDING,
                    });
                }
            } catch (e) {
                // Sync throws (very rare — most validation errors are async).
                // Pop the scope to keep the device clean and demote.
                void this.device.popErrorScope().catch(() => { /* ignore */ });
                this.markComputeFailed(`compute pipeline creation threw: ${String(e)}`);
                for (const d of createdDummies)
                    if (d) try { d.destroy(); } catch { /* ignore */ }
                if (createdBuffer) try { createdBuffer.destroy(); } catch { /* ignore */ }
                return;
            }
            const err = await this.device.popErrorScope();
            if (err) {
                this.markComputeFailed(`compute pipeline rejected at init: ${err.message}`);
                for (const d of createdDummies)
                    if (d) try { d.destroy(); } catch { /* ignore */ }
                try { createdBuffer.destroy(); } catch { /* ignore */ }
                return;
            }
            this.computeBgLayout = createdLayout;
            this.computePipeline = createdPipeline;
            this.computeUniformBuffer = createdBuffer;
            this.computeUniformStaging = new ArrayBuffer(COMPUTE_UNIFORM_BYTES);
            this.dummyStorageTexs = createdDummies.slice();
            this.dummyStorageViews = createdDummies.map(d => d!.createView());
            infoLog?.log(`Downscaler compute path armed (format=${this.format})`);
        })();
        return this.computePipelinePromise;
    }

    private markComputeFailed(reason: string): void {
        this.useStoragePath = false;
        this.computePipelineFailed = true;
        warnLog?.log(`Downscaler ${reason} — using render path`);
    }

    private ensureUniformCapacity(slotCount: number): void {
        const neededBytes = Math.max(slotCount, 1) * this.slotStride;
        if (this.uniformBuffer && this.uniformBufferCapacity >= neededBytes) return;
        if (this.uniformBuffer)
            this.disposeBuffer(this.uniformBuffer);
        this.uniformBuffer = this.device.createBuffer({
            size: neededBytes,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        this.uniformBufferCapacity = neededBytes;
    }

    private disposeSlots(): void {
        for (const slot of this.slots) {
            this.disposeSlot(slot);
        }
        this.slots = [];
    }

    private disposeSlot(slot: TargetSlot): void {
        if (this.activeSubmissions > 0) {
            this.deferredSlotDisposals.push(slot);
            return;
        }
        try { slot.ctx.unconfigure(); } catch { /* ignore */ }
        if (slot.storageTex) {
            try { slot.storageTex.destroy(); } catch { /* ignore */ }
            slot.storageTex = null;
            slot.storageView = null;
        }
    }

    private disposeBuffer(buffer: GPUBuffer): void {
        if (this.activeSubmissions > 0) {
            this.deferredBufferDisposals.push(buffer);
            return;
        }
        buffer.destroy();
    }

    private flushDeferredDisposals(): void {
        if (this.activeSubmissions > 0) return;
        for (const slot of this.deferredSlotDisposals) {
            try { slot.ctx.unconfigure(); } catch { /* ignore */ }
            if (slot.storageTex) {
                try { slot.storageTex.destroy(); } catch { /* ignore */ }
                slot.storageTex = null;
                slot.storageView = null;
            }
        }
        this.deferredSlotDisposals = [];
        for (const buffer of this.deferredBufferDisposals)
            buffer.destroy();
        this.deferredBufferDisposals = [];
    }
}

import { from, type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import { DeviceOrientation, ScreenOrientation, normalizeRotationQuarter, type RotationQuarter } from 'orientation';
import type { CapturedBundle, CapturedFrame, NormalizedFrame } from '../frame-envelopes';
import { cameraRotationDeg } from '../orientation/quantize';
import { drawFrameCover, resizeCanvas } from '../canvas/resize';
import { CanvasDownscaler } from '../canvas/downscaler';
import { WebGlDownscaler, probeWebGl2 } from '../webgl/downscaler';
import { parallelMap } from './parallel-map';
import type { LayerLadderController } from '../sender/layer-ladder-controller';

const { warnLog } = getLogs('VideoPipeline');

export interface LayerSpec {
    width: number;
    height: number;
}

// `normalize` produces the full-res surface that BOTH the self-preview clone and
// `spatialize` consume, so its target is the fixed display CEILING (capture-derived
// full-ladder top), NOT the active-ladder top. This keeps the self-view full-res even
// when the active encode ladder shrinks toward L0; `spatialize` owns all per-tier
// downscaling from this ceiling frame. `getNormalizeSize` is a thunk so a mid-stream
// orientation flip (which swaps the ceiling W/H) is picked up without a pipeline rebuild.
export interface NormalizeFrameOptions {
    getNormalizeSize: () => LayerSpec;
    isCamera: boolean;
    isFrontCamera: boolean;
    isIos: boolean;
}

// Contract: one frame per spec in order; implementation owns the input frame
// (consumes/closes it, including on failure before throwing). The top tier
// (last spec) is the input passed through unchanged; lower tiers are real
// pixel downscales (coded == target).
export interface DownscalerLike {
    process(
        input: VideoFrame,
        layers: readonly LayerSpec[],
    ): Promise<VideoFrame[]>;
    dispose?(): void;
}

// Layer set comes from the ladder controller and may grow/shrink mid-stream.
// Bottom-first: configs[0] is the base layer; the top layer must match the
// normalized frame dimensions.
export interface DownscaleOptions {
    controller: LayerLadderController;
    // Lazy-init per slot so construction runs after the worker (and any GPU)
    // exists. Defaults to WebGL2 with a Canvas2D fallback.
    createDownscaler?: () => DownscalerLike;
    // Number of frames whose downscale may be in flight at once, each on its own
    // downscaler instance (its own WebGL context/canvas). >1 overlaps a frame's
    // GPU downscale with the previous frame's encode and the next frame's
    // capture, so a slow per-frame `new VideoFrame(canvas)` round-trip (Android)
    // doesn't cap capture throughput. Default 2.
    concurrency?: number;
    // On hang: detach the stuck process(), recreate the downscaler on the next
    // frame, and force a keyframe so encoders re-anchor. Default 1500 ms.
    hangTimeoutMs?: number;
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
}

interface DownscaleSlotState {
    downscaler: DownscalerLike | null;
}

// WebGL2 real downscale with a Canvas2D fallback. Re-probes WebGL each call so
// a session-level disable (context-lost) routes new instances to Canvas2D.
export function createDefaultDownscaler(): DownscalerLike {
    if (probeWebGl2()) {
        try {
            return new WebGlDownscaler();
        } catch (e) {
            warnLog?.log('createDefaultDownscaler: WebGL init failed, using Canvas2D:', e);
        }
    }
    return new CanvasDownscaler();
}

interface FrameTransform {
    cropboxRotation: RotationQuarter;
    wireRotation: RotationQuarter;
}

interface Slot {
    canvas: OffscreenCanvas;
    ctx: OffscreenCanvasRenderingContext2D;
}

// CapturedFrame -> NormalizedFrame. Direct generator — no parallelMap, no
// extra await tick. When the source frame already matches the top-layer dims
// and no cropbox rotation is needed (the common desktop / OBS path), this
// degenerates to a zero-allocation pass-through: the input frame flows
// through unchanged. The expensive `new VideoFrame(canvas)` allocation only
// happens when an actual crop/resize/rotate is required.
//
// iOS path: rotation is never baked into pixels (cropboxRotation always 0)
// but `wireRotation` must be decided per frame from `ScreenOrientation`,
// and the camera may not deliver target dims natively. Full per-frame
// orientation.decide() + identity short-circuit + cover-crop fallback.
//
// Why this matters: per-frame `new VideoFrame(OffscreenCanvas)` at top dims
// (1280×720+) builds GPU-texture pressure that strangles the HW encoder over
// time. Avoiding the allocation on the common path is the single most
// impactful sender-side optimisation.
export function normalizeFrame(opts: NormalizeFrameOptions): PipeOperator<CapturedFrame, NormalizedFrame> {
    const orientation = new NormalizeFrameOrientation(opts);
    let slot: Slot | null = null;
    // Cached ceiling LayerSpec; refreshed when getNormalizeSize() changes (e.g.
    // an orientation flip swaps W/H).
    let target: LayerSpec = opts.getNormalizeSize();

    return source => {
        async function* impl(): AsyncIterable<NormalizedFrame> {
            try {
                for await (const envelope of source) {
                    const ns = opts.getNormalizeSize();
                    if (ns.width !== target.width || ns.height !== target.height)
                        target = ns;
                    const input = envelope.frame;
                    const transform = orientation.decide(input, target);
                    const displayW = input.displayWidth || input.codedWidth;
                    const displayH = input.displayHeight || input.codedHeight;
                    if (transform.cropboxRotation === 0
                        && displayW === target.width
                        && displayH === target.height
                        && input.codedWidth === target.width
                        && input.codedHeight === target.height) {
                        // True identity: pass envelope through. Only patch
                        // `rotation` when it actually changes — preserves
                        // object identity when nothing changed.
                        yield envelope.rotation === transform.wireRotation
                            ? envelope
                            : { ...envelope, rotation: transform.wireRotation };
                        continue;
                    }
                    // Zero-copy re-crop via VideoFrame constructor + explicit
                    // visibleRect + displayWidth/Height. Chrome's MSTP
                    // crop-and-scale gives us a frame with coded = native
                    // sensor (e.g. 1920×1080) and display = scaled output
                    // (e.g. 1280×720), but visibleRect spans the entire
                    // coded plane — so the encoder, reading from the coded
                    // plane, encodes the wrong region. We construct a new
                    // VideoFrame referencing the same buffer with a centered
                    // visibleRect at the target aspect; Chrome's encoder
                    // honors that (verified empirically), producing the
                    // correct crop without a `new VideoFrame(canvas)`
                    // roundtrip. Saves ~0.5 ms/frame of canvas work and the
                    // associated GPU texture allocation.
                    if (transform.cropboxRotation === 0 && input.codedWidth > 0 && input.codedHeight > 0) {
                        const visible = computeCoverVisibleRect(
                            input.codedWidth, input.codedHeight,
                            target.width, target.height);
                        const recropped = new VideoFrame(input, {
                            visibleRect: visible,
                            displayWidth: target.width,
                            displayHeight: target.height,
                            timestamp: input.timestamp,
                        });
                        try { input.close(); } catch { /* already closed */ }
                        yield { ...envelope, frame: recropped, rotation: transform.wireRotation };
                        continue;
                    }
                    // Fallback path — used when cropbox rotation is needed
                    // (Android portrait flip etc.), since VideoFrame's
                    // zero-copy constructor cannot rotate pixels. Pay the
                    // canvas + new VideoFrame cost only on this branch.
                    slot ??= createSlot('normalizeFrame');
                    prepareSlot(slot, target.width, target.height);
                    drawFrameCover(slot.ctx, input, target.width, target.height, transform.cropboxRotation);
                    const out = new VideoFrame(slot.canvas, {
                        timestamp: input.timestamp,
                        alpha: 'discard',
                    });
                    try { input.close(); } catch { /* already closed */ }
                    yield { ...envelope, frame: out, rotation: transform.wireRotation };
                }
            } finally {
                if (slot) {
                    slot.canvas.width = 0;
                    slot.canvas.height = 0;
                    slot = null;
                }
            }
        }
        return from(impl());
    };
}

// NormalizedFrame -> CapturedBundle. Per-layer envelopes share all metadata
// from the input — only `frame` differs. Output bottom-first. Each lower tier
// is a REAL pixel downscale (coded == target) produced by the injected
// downscaler (WebGL2, Canvas2D fallback); the top tier is the input passed
// through. Up to `concurrency` bundles run process() in parallel, each on its
// own downscaler instance; ordering preserved via parallelMap. The downscaler
// owns the input frame; the operator owns the produced layer frames and closes
// them on any pre-yield error.
export function downscale(opts: DownscaleOptions): PipeOperator<NormalizedFrame, CapturedBundle> {
    const createDownscaler = opts.createDownscaler ?? createDefaultDownscaler;
    const concurrency = Math.max(1, opts.concurrency ?? 2);
    const hangTimeoutMs = opts.hangTimeoutMs ?? 1_500;
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms): unknown => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn
        ?? ((h: unknown): void => clearTimeout(h as ReturnType<typeof setTimeout>));

    // Shared across slots: single-threaded JS keeps the read-modify-write between
    // awaits atomic, so a hang on one slot can force a keyframe on the next bundle.
    let consecutiveHangs = 0;
    let forceKeyframeAfterHang = false;
    const slots: (DownscaleSlotState | undefined)[] =
        new Array<DownscaleSlotState | undefined>(concurrency).fill(undefined);

    return source => {
        const stage = parallelMap<NormalizedFrame, CapturedBundle | null>({
            concurrency,
            onSlotInit: slotId => { slots[slotId] = { downscaler: null }; },
            onSlotDispose: slotId => {
                const s = slots[slotId];
                if (s?.downscaler && typeof s.downscaler.dispose === 'function')
                    try { s.downscaler.dispose(); } catch { /* ignore */ }
                slots[slotId] = undefined;
            },
            onUnconsumedResult: bundle => { if (bundle) closeBundleLayers(bundle); },
            map: async (envelope, slotId) => {
                // Snapshot the ladder per frame so QC hot-applies (layer grow/
                // shrink) take effect; encode() aligns to bundle.layers.length.
                const layers: LayerSpec[] = opts.controller.current.configs.map(
                    c => ({ width: c.width, height: c.height }));
                const slot = slots[slotId]!;
                slot.downscaler ??= createDownscaler();
                const downscaler = slot.downscaler;

                const startedAtMs = performance.now();
                const processPromise = downscaler.process(envelope.frame, layers);
                let timerHandle: unknown = null;
                const timeoutP = new Promise<'timeout'>(resolve => {
                    timerHandle = setTimeoutFn(() => resolve('timeout'), hangTimeoutMs);
                });
                let raced: VideoFrame[] | 'timeout';
                try {
                    raced = await Promise.race([processPromise, timeoutP]);
                } finally {
                    if (timerHandle !== null)
                        try { clearTimeoutFn(timerHandle); } catch { /* ignore */ }
                }
                if (raced === 'timeout') {
                    consecutiveHangs++;
                    forceKeyframeAfterHang = true;
                    // Detach: close whatever the stuck process() eventually
                    // produces (it owns the input frame); drop this slot's
                    // downscaler so the next frame recreates it.
                    void processPromise.then(closeFrames, () => { /* logged */ });
                    const stuck = slot.downscaler;
                    slot.downscaler = null;
                    if (typeof stuck.dispose === 'function')
                        try { stuck.dispose(); } catch { /* ignore */ }
                    if (consecutiveHangs >= 4)
                        throw new Error(
                            `downscale: hang watchdog fired ${consecutiveHangs}x in a row, giving up`);
                    return null;
                }
                consecutiveHangs = 0;
                const frames = raced;
                if (frames.length !== layers.length) {
                    closeFrames(frames);
                    throw new Error(
                        `downscale: downscaler returned ${frames.length} frames, expected ${layers.length}`);
                }
                const ms = performance.now() - startedAtMs;
                const stats = envelope.stats;
                stats.downscaleTimeMsSum += ms;
                stats.downscaleTimeMsCount++;
                if (ms > stats.downscaleTimeMsMax) stats.downscaleTimeMsMax = ms;
                // After a hang, re-anchor every encoder with a keyframe.
                const layerSource = forceKeyframeAfterHang
                    ? { ...envelope, forceKeyframe: true }
                    : envelope;
                forceKeyframeAfterHang = false;
                return {
                    layers: frames.map(frame => makeLayerEnvelope(layerSource, frame)),
                    index: envelope.index,
                    dropTrace: envelope.dropTrace,
                    rotation: envelope.rotation,
                    stats: envelope.stats,
                };
            },
        });

        // Skip post-hang null entries (input consumed, no bundle produced).
        const op = stage(source);
        return from((async function* (): AsyncIterable<CapturedBundle> {
            for await (const bundle of op)
                if (bundle !== null) yield bundle;
        })());
    };
}

function closeFrames(frames: readonly VideoFrame[]): void {
    for (const frame of frames) {
        try { frame.close(); } catch { /* ignore */ }
    }
}

function closeBundleLayers(bundle: CapturedBundle): void {
    for (const layer of bundle.layers) {
        try { layer.frame.close(); } catch { /* ignore */ }
    }
}

class NormalizeFrameOrientation {
    private initialDeviceAngle: number | null = null;
    private currentCropboxRotation: RotationQuarter = 0;

    constructor(private readonly opts: NormalizeFrameOptions) {}

    decide(input: VideoFrame, target: LayerSpec): FrameTransform {
        if (!this.opts.isCamera)
            return { cropboxRotation: 0, wireRotation: 0 };

        return this.opts.isIos
            ? this.decideIosTransform(input)
            : this.decideChromeStyleTransform(input, target);
    }

    // iOS never bakes a rotation into pixels — the cropbox always matches the
    // frame and only `wireRotation` carries the orientation downstream.
    // Wire rotation comes from one of two sources, in order:
    //   1. ScreenOrientation, once it's been observed (initial platform read,
    //      a `screen.orientation` change, or SharedSettings hydration in a
    //      worker). On iOS Safari main thread `window.orientation` is always
    //      readable so this is the normal path.
    //   2. Buffer-vs-display dim comparison on the frame itself — used only
    //      during the worker startup window before SharedSettings hydrates.
    //      Transposed dims (landscape coded buffer, portrait display) signal
    //      portrait ⇒ apply the +90° rotation. Matched dims ⇒ no rotation.
    private decideIosTransform(input: VideoFrame): FrameTransform {
        let wireRotation: RotationQuarter;
        if (ScreenOrientation.isObserved) {
            wireRotation = normalizeRotationQuarter(
                cameraRotationDeg(ScreenOrientation.quarter * 90, this.opts.isFrontCamera) / 90);
        } else if (isFrameTransposed(input)) {
            wireRotation = normalizeRotationQuarter(
                cameraRotationDeg(0, this.opts.isFrontCamera) / 90);
        } else {
            wireRotation = 0;
        }
        return { cropboxRotation: 0, wireRotation };
    }

    private decideChromeStyleTransform(input: VideoFrame, target: LayerSpec): FrameTransform {
        const cropboxRotation = this.pickCropboxRotation(input, target);
        return {
            cropboxRotation,
            // Android Chrome/WebView already handles the hidden 180-degree case by
            // keeping/returning the frame upright. For visible +/-90 dimension
            // flips, rotate the receiver by the inverse of the baked pixel turn.
            wireRotation: normalizeRotationQuarter(-cropboxRotation),
        };
    }

    private pickCropboxRotation(input: VideoFrame, target: LayerSpec): RotationQuarter {
        const sourceIsPortrait = input.codedHeight > input.codedWidth;
        const targetIsPortrait = target.height > target.width;
        if (sourceIsPortrait === targetIsPortrait) {
            this.currentCropboxRotation = 0;
            return 0;
        }

        if (this.currentCropboxRotation !== 0)
            return this.currentCropboxRotation;

        this.initialDeviceAngle ??= DeviceOrientation.angle;
        const deltaDeg = signedAngleDeltaDeg(this.initialDeviceAngle, DeviceOrientation.angle)
            * (this.opts.isFrontCamera ? 1 : -1);
        this.currentCropboxRotation = deltaDeg < 0 ? 3 : 1;
        return this.currentCropboxRotation;
    }
}

// Returns a centered visibleRect with the target's aspect ratio that fits
// inside the coded plane (cover-crop). Aspect match → full coded plane.
function computeCoverVisibleRect(
    codedW: number, codedH: number,
    targetW: number, targetH: number,
): { x: number; y: number; width: number; height: number } {
    const codedAspect = codedW / codedH;
    const targetAspect = targetW / targetH;
    let vw: number, vh: number;
    if (Math.abs(codedAspect - targetAspect) < 1e-3) {
        vw = codedW;
        vh = codedH;
    } else if (codedAspect > targetAspect) {
        vh = codedH;
        vw = Math.round(vh * targetAspect);
    } else {
        vw = codedW;
        vh = Math.round(vw / targetAspect);
    }
    // VideoFrame visibleRect spec: x/y/width/height in coded pixel units,
    // must be integer-aligned to subsampling (typically 2 for YUV).
    const align = (n: number): number => n & ~1;
    vw = align(vw);
    vh = align(vh);
    const x = align(Math.floor((codedW - vw) / 2));
    const y = align(Math.floor((codedH - vh) / 2));
    return { x, y, width: vw, height: vh };
}

function createSlot(owner: string): Slot {
    const canvas = new OffscreenCanvas(0, 0);
    const ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
    if (!ctx)
        throw new Error(`${owner}: 2D context unavailable on OffscreenCanvas`);

    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'medium';
    return { canvas, ctx };
}

function prepareSlot(slot: Slot, width: number, height: number): void {
    resizeCanvas(slot.canvas, width, height);
}

function isFrameTransposed(input: VideoFrame): boolean {
    const cw = input.codedWidth;
    const ch = input.codedHeight;
    const dw = input.displayWidth;
    const dh = input.displayHeight;
    return cw > 0 && ch > 0 && dw > 0 && dh > 0 && cw === dh && ch === dw;
}

function signedAngleDeltaDeg(from: number, to: number): number {
    if (!Number.isFinite(from) || !Number.isFinite(to))
        return 0;
    return ((((to - from + 180) % 360) + 360) % 360) - 180;
}

function makeLayerEnvelope(source: NormalizedFrame, frame: VideoFrame): CapturedFrame {
    return {
        frame,
        capturedAt: source.capturedAt,
        index: source.index,
        dropTrace: source.dropTrace,
        sourceWidth: source.sourceWidth,
        sourceHeight: source.sourceHeight,
        forceKeyframe: source.forceKeyframe,
        rotation: source.rotation,
        stats: source.stats,
    };
}

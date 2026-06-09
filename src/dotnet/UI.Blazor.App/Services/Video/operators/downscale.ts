import { from, type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import { DeviceOrientation, ScreenOrientation, normalizeRotationQuarter, type RotationQuarter } from 'orientation';
import type { CapturedBundle, CapturedFrame, NormalizedFrame } from '../frame-envelopes';
import { cameraRotationDeg } from '../orientation/quantize';
import { drawFrameCover, resizeCanvas } from '../canvas/resize';
import { CanvasDownscaler } from '../canvas/downscaler';
import { MetadataDownscaler } from '../metadata/downscaler';
import { WebGlDownscaler, probeWebGl2 } from '../webgl/downscaler';
import { WebGpuDownscaler } from '../webgpu/downscaler';
import { parallelMap } from './parallel-map';
import type { PreviewSink } from './preview-forwarder';
import type { LayerLadderController } from '../sender/layer-ladder-controller';

const { warnLog } = getLogs('VideoPipeline');

export interface LayerSpec {
    width: number;
    height: number;
}

// Downscaler backend, selectable from the video diagnostics toggle. `metadata`
// is the default cheap display-size wrap (coded stays at the ceiling, the HW
// encoder rescales) — keeps the downscale off the GPU. `webgl` is the GPU cascade
// (Canvas2D fallback if WebGL2 is unavailable) and `canvas` forces the Canvas2D
// cascade; both are the fallback for HW that top-left-crops instead of scaling
// (notably Edge HEVC). See docs/live-video/02-sender.md.
// 'webgpu' downscales on the GPU AND outputs NV12 at the tier size, so the HW
// encoder skips its internal libyuv scale + RGBA→NV12 convert (the readback
// remains, but shrinks to a target-size NV12 buffer). Falls back to 'metadata'
// when WebGPU is unavailable. See webgpu/downscaler.ts.
export type DownscalerMode = 'webgl' | 'canvas' | 'metadata' | 'webgpu' | 'webgpu-2pass';

// Contract: one frame per spec in order (bottom-first). A spec matching the
// input's display dims returns the input frame as that tier (shared reference);
// every other tier is a real coded == target downscale, cascaded top→bottom.
// The implementation does NOT own the input — it neither closes it on success
// nor on failure; the caller (normalizeDownscale) owns the ceiling frame.
export interface DownscalerLike {
    process(
        input: VideoFrame,
        layers: readonly LayerSpec[],
    ): Promise<VideoFrame[]>;
    dispose?(): void;
}

// Fused normalize + downscale. CapturedFrame -> CapturedBundle in one stage:
// produce the full-res normalized ceiling (crop/orientation baked, the
// self-preview surface) and every encode tier from it, emitting both on the
// bundle. The ceiling target is the fixed display CEILING (`getNormalizeSize`,
// the full-ladder top), independent of the active encode ladder, so the
// self-view stays full-res when the active ladder shrinks toward L0.
// `getNormalizeSize` is a thunk so a mid-stream orientation flip (which swaps
// the ceiling W/H) is picked up without a pipeline rebuild.
//
// Sits AFTER temporalPace, so the ceiling (and therefore the self-preview) is
// produced only for frames that survive demand pacing — the simplest fusion:
// one normalize feeds preview + every tier. The ceiling is owned entirely
// within this stage: it is the top tier (closed downstream by encode) when the
// active top matches it, otherwise an orphan this stage closes after the
// preview tap. It never escapes onto the bundle.
export interface NormalizeDownscaleOptions {
    controller: LayerLadderController;
    getNormalizeSize: () => LayerSpec;
    isCamera: boolean;
    isFrontCamera: boolean;
    isIos: boolean;
    // Self-preview tap. Fed a clone of the full-res ceiling per kept frame —
    // the ceiling only exists inside this stage, so the tap lives here rather
    // than as a downstream operator over the bundle. Omit to skip preview.
    preview?: PreviewSink;
    // Backend selection (diagnostics toggle). Default 'webgl'.
    mode?: DownscalerMode;
    // Test override; takes precedence over `mode`. Lazy-init per slot so
    // construction runs after the worker (and any GPU) exists.
    createDownscaler?: () => DownscalerLike;
    // Number of frames whose downscale may be in flight at once, each on its own
    // downscaler instance (its own WebGL context/canvas) and its own normalize
    // canvas. >1 overlaps a frame's GPU work with the previous frame's encode and
    // the next frame's capture, so a slow per-frame `new VideoFrame(canvas)`
    // round-trip (Android) doesn't cap capture throughput. Default 2.
    concurrency?: number;
    // On hang: detach the stuck process(), recreate the downscaler on the next
    // frame, and force a keyframe so encoders re-anchor. Default 1500 ms.
    hangTimeoutMs?: number;
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
}

interface Slot {
    canvas: OffscreenCanvas;
    ctx: OffscreenCanvasRenderingContext2D;
}

interface DownscaleSlotState {
    downscaler: DownscalerLike | null;
    // Per-slot 2D canvas for the rotate fallback ceiling render (cannot be
    // shared across concurrent frames).
    normSlot: Slot | null;
}

interface FrameTransform {
    cropboxRotation: RotationQuarter;
    wireRotation: RotationQuarter;
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

export function createDownscalerForMode(mode: DownscalerMode): DownscalerLike {
    switch (mode) {
    case 'canvas':
        return new CanvasDownscaler();
    case 'metadata':
        return new MetadataDownscaler();
    case 'webgpu':
        // Self-falls-back to metadata when WebGPU is unavailable / device lost.
        return new WebGpuDownscaler();
    case 'webgpu-2pass':
        // Same NV12 output, but forces the render two-pass path (compute disabled) —
        // a diagnostic A/B against the compute path at full rate.
        return new WebGpuDownscaler({ forceRender: true });
    case 'webgl':
    default:
        return createDefaultDownscaler();
    }
}

// CapturedFrame -> CapturedBundle. Produces the normalized ceiling (identity
// pass-through on the common desktop/OBS path, zero-copy VideoFrame re-crop when
// only a crop is needed, canvas render only when a cropbox rotation must be
// baked) and the per-tier downscales (bottom-first) from it, in one stage. Up to
// `concurrency` frames run in parallel, each on its own downscaler + normalize
// canvas; ordering preserved via parallelMap. The operator owns the ceiling and
// the produced tier frames; the only closer of the ceiling-when-orphan is the
// downstream previewForwarder, of the tiers is the encode stage.
export function normalizeDownscale(opts: NormalizeDownscaleOptions): PipeOperator<CapturedFrame, CapturedBundle> {
    const orientation = new NormalizeFrameOrientation(opts);
    const mode = opts.mode ?? 'webgl';
    const createDownscaler = opts.createDownscaler ?? (() => createDownscalerForMode(mode));
    const concurrency = Math.max(1, opts.concurrency ?? 2);
    const hangTimeoutMs = opts.hangTimeoutMs ?? 1_500;
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms): unknown => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn
        ?? ((h: unknown): void => clearTimeout(h as ReturnType<typeof setTimeout>));

    // Cached ceiling LayerSpec; refreshed when getNormalizeSize() changes (e.g.
    // an orientation flip swaps W/H).
    let target: LayerSpec = opts.getNormalizeSize();

    // Shared across slots: single-threaded JS keeps the read-modify-write between
    // awaits atomic, so a hang on one slot can force a keyframe on the next bundle.
    let consecutiveHangs = 0;
    let forceKeyframeAfterHang = false;
    let backendLogged = false;
    const slots: (DownscaleSlotState | undefined)[] =
        new Array<DownscaleSlotState | undefined>(concurrency).fill(undefined);

    return source => {
        const stage = parallelMap<CapturedFrame, CapturedBundle | null>({
            concurrency,
            onSlotInit: slotId => { slots[slotId] = { downscaler: null, normSlot: null }; },
            onSlotDispose: slotId => {
                const s = slots[slotId];
                if (s?.downscaler && typeof s.downscaler.dispose === 'function')
                    try { s.downscaler.dispose(); } catch { /* ignore */ }
                if (s?.normSlot) {
                    s.normSlot.canvas.width = 0;
                    s.normSlot.canvas.height = 0;
                }
                slots[slotId] = undefined;
            },
            onUnconsumedResult: bundle => { if (bundle) closeBundle(bundle); },
            map: async (envelope, slotId) => {
                const slot = slots[slotId]!;
                const ns = opts.getNormalizeSize();
                if (ns.width !== target.width || ns.height !== target.height)
                    target = ns;
                const transform = orientation.decide(envelope.frame, target);
                const startedAtMs = performance.now();
                // 1. Normalize → ceiling (consumes envelope.frame; identity path
                //    returns it unchanged).
                const ceiling = produceCeiling(envelope.frame, target, transform.cropboxRotation, () => {
                    slot.normSlot ??= createSlot('normalizeDownscale');
                    return slot.normSlot;
                });

                // 2. Tiers from the ceiling, on the selected backend, watchdogged.
                const layers: LayerSpec[] = opts.controller.current.configs.map(
                    c => ({ width: c.width, height: c.height }));
                slot.downscaler ??= createDownscaler();
                const downscaler = slot.downscaler;
                // Report the resolved backend once — the requested mode plus the
                // actual class, since 'webgl' falls back to Canvas2D when WebGL2
                // is unavailable / its context was lost.
                if (!backendLogged) {
                    backendLogged = true;
                    warnLog?.log(`normalizeDownscale: downscaler backend = ${downscaler.constructor.name} (mode '${mode}')`);
                }
                const processPromise = downscaler.process(ceiling, layers);
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
                    // produces; close the ceiling we own.
                    void processPromise.then(closeFrames, () => { /* logged */ });
                    try { ceiling.close(); } catch { /* ignore */ }
                    const stuck = slot.downscaler;
                    slot.downscaler = null;
                    if (typeof stuck.dispose === 'function')
                        try { stuck.dispose(); } catch { /* ignore */ }
                    if (consecutiveHangs >= 4)
                        throw new Error(
                            `normalizeDownscale: hang watchdog fired ${consecutiveHangs}x in a row, giving up`);
                    return null;
                }
                consecutiveHangs = 0;
                const frames = raced;
                if (frames.length !== layers.length) {
                    closeFrames(frames);
                    if (!frames.includes(ceiling))
                        try { ceiling.close(); } catch { /* ignore */ }
                    throw new Error(
                        `normalizeDownscale: downscaler returned ${frames.length} frames, expected ${layers.length}`);
                }
                const ms = performance.now() - startedAtMs;
                const stats = envelope.stats;
                stats.downscaleTimeMsSum += ms;
                stats.downscaleTimeMsCount++;
                if (ms > stats.downscaleTimeMsMax) stats.downscaleTimeMsMax = ms;
                // After a hang, re-anchor every encoder with a keyframe.
                const wireRotation = transform.wireRotation;
                // Preview taps a clone of the ceiling (full-res, never the
                // shrunk tier). Then close the ceiling iff it is not also a tier
                // — encode closes the tiers (incl. the ceiling-as-top-tier case).
                opts.preview?.forward(ceiling, wireRotation);
                if (!frames.includes(ceiling))
                    try { ceiling.close(); } catch { /* ignore */ }
                const layerSource: NormalizedFrame = {
                    ...envelope,
                    forceKeyframe: forceKeyframeAfterHang || envelope.forceKeyframe,
                    rotation: wireRotation,
                };
                forceKeyframeAfterHang = false;
                return {
                    layers: frames.map(frame => makeLayerEnvelope(layerSource, frame)),
                    index: envelope.index,
                    dropTrace: envelope.dropTrace,
                    rotation: wireRotation,
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

// Produce the normalized ceiling frame from the captured input, applying the
// decided crop/rotation. Consumes `input` (closes it) whenever it allocates a
// new frame; returns `input` unchanged on the true-identity path.
//   * identity        — capture already matches the ceiling, no crop/rotate.
//   * zero-copy recrop — crop only (cropboxRotation 0): a new VideoFrame sharing
//                        the buffer with a centered visibleRect at ceiling aspect.
//   * canvas render    — a cropbox rotation must be baked (Android portrait flip).
function produceCeiling(
    input: VideoFrame,
    target: LayerSpec,
    cropboxRotation: RotationQuarter,
    getSlot: () => Slot,
): VideoFrame {
    const displayW = input.displayWidth || input.codedWidth;
    const displayH = input.displayHeight || input.codedHeight;
    if (cropboxRotation === 0
        && displayW === target.width
        && displayH === target.height
        && input.codedWidth === target.width
        && input.codedHeight === target.height) {
        return input;
    }
    if (cropboxRotation === 0 && input.codedWidth > 0 && input.codedHeight > 0) {
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
        return recropped;
    }
    const slot = getSlot();
    prepareSlot(slot, target.width, target.height);
    drawFrameCover(slot.ctx, input, target.width, target.height, cropboxRotation);
    const out = new VideoFrame(slot.canvas, {
        timestamp: input.timestamp,
        alpha: 'discard',
    });
    try { input.close(); } catch { /* already closed */ }
    return out;
}

function closeFrames(frames: readonly VideoFrame[]): void {
    for (const frame of frames) {
        try { frame.close(); } catch { /* ignore */ }
    }
}

function closeBundle(bundle: CapturedBundle): void {
    // The ceiling-when-orphan was already closed in map() before the bundle was
    // produced; the ceiling-as-top-tier is one of these layers.
    for (const layer of bundle.layers) {
        try { layer.frame.close(); } catch { /* ignore */ }
    }
}

class NormalizeFrameOrientation {
    private initialDeviceAngle: number | null = null;
    private currentCropboxRotation: RotationQuarter = 0;

    constructor(private readonly opts: { isCamera: boolean; isFrontCamera: boolean; isIos: boolean }) {}

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

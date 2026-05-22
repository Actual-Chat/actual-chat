import { from, type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import { DeviceOrientation, ScreenOrientation, normalizeRotationQuarter, type RotationQuarter } from 'orientation';
import type { CapturedBundle, CapturedFrame, NormalizedFrame } from '../frame-envelopes';
import { cameraRotationDeg } from '../orientation/quantize';
import { drawFrameCover, resizeCanvas } from '../canvas/resize';
import type { LayerLadderController } from '../sender/layer-ladder-controller';

const { warnLog } = getLogs('VideoPipeline');

export interface LayerSpec {
    width: number;
    height: number;
}

// Operator receives either a static top-layer target (tests, single-shot setups)
// or a LayerLadderController whose top-layer dims may change mid-stream.
export interface NormalizeFrameOptions {
    target?: LayerSpec;
    ladder?: LayerLadderController;
    isCamera: boolean;
    isFrontCamera: boolean;
    isIos: boolean;
}

// Operator receives either a static ladder snapshot (tests, single-shot setups)
// or a LayerLadderController whose ladder may grow/shrink mid-stream.
export interface SpatializeOptions {
    // Bottom-first: ladder[0] is the base layer. The top layer must match the
    // normalized frame dimensions.
    ladder?: readonly LayerSpec[];
    controller?: LayerLadderController;
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
    if (!opts.target && !opts.ladder)
        throw new Error('normalizeFrame: requires `target` or `ladder`');
    const orientation = new NormalizeFrameOrientation(opts);
    let slot: Slot | null = null;
    // Cached top-layer LayerSpec; refreshed when controller.version bumps.
    let target: LayerSpec = opts.target
        ? { ...opts.target }
        : topLayerOf(opts.ladder!);
    let lastSeenVersion = opts.ladder?.current.version ?? -1;

    return source => {
        async function* impl(): AsyncIterable<NormalizedFrame> {
            try {
                for await (const envelope of source) {
                    if (opts.ladder) {
                        const cur = opts.ladder.current;
                        if (cur.version !== lastSeenVersion) {
                            target = topLayerOf(opts.ladder);
                            lastSeenVersion = cur.version;
                        }
                    }
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

// NormalizedFrame -> CapturedBundle. Direct generator — no parallelMap.
// Single-layer ladder: input becomes the only layer (zero allocation).
// Multi-layer ladder: each lower layer gets one canvas downscale +
// `new VideoFrame`; the top layer stays as the input frame.
export function spatialize(opts: SpatializeOptions): PipeOperator<NormalizedFrame, CapturedBundle> {
    if (!opts.ladder && !opts.controller)
        throw new Error('spatialize: requires `ladder` or `controller`');
    if (opts.ladder?.length === 0)
        throw new Error('spatialize: ladder must contain at least one layer');

    // When a static ladder is passed, snapshot it; when a controller is
    // passed, the processor re-reads on every frame and `shrinkTo` is
    // invoked on version bumps that lower the layer count.
    const staticLadder = opts.ladder ? opts.ladder.slice() : null;
    let lastSeenVersion = opts.controller?.current.version ?? -1;
    let processor: SpatializeSlotProcessor | null = null;

    return source => {
        async function* impl(): AsyncIterable<CapturedBundle> {
            try {
                for await (const frame of source) {
                    processor ??= new SpatializeSlotProcessor();
                    let ladder: readonly LayerSpec[];
                    if (opts.controller) {
                        const cur = opts.controller.current;
                        if (cur.version !== lastSeenVersion) {
                            // Drop excess slots when the ladder shrank so we
                            // don't carry stale canvases past their last use.
                            processor.shrinkTo(cur.configs.length);
                            lastSeenVersion = cur.version;
                        }
                        ladder = configsToLadder(cur.configs);
                    } else {
                        ladder = staticLadder!;
                    }
                    yield await processor.process(frame, ladder);
                }
            } finally {
                processor?.dispose();
                processor = null;
            }
        }
        return from(impl());
    };
}

function topLayerOf(controller: LayerLadderController): LayerSpec {
    const cfg = controller.current.configs[controller.current.configs.length - 1];
    return { width: cfg.width, height: cfg.height };
}

function configsToLadder(configs: readonly { width: number; height: number }[]): LayerSpec[] {
    const out = new Array<LayerSpec>(configs.length);
    for (let i = 0; i < configs.length; i++)
        out[i] = { width: configs[i].width, height: configs[i].height };
    return out;
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

class SpatializeSlotProcessor {
    private readonly slots: Slot[] = [];

    process(input: NormalizedFrame, ladder: readonly LayerSpec[]): Promise<CapturedBundle> {
        const topIdx = ladder.length - 1;
        const layers = new Array<CapturedFrame | null>(ladder.length).fill(null);
        layers[topIdx] = input;
        try {
            if (topIdx > 0)
                this.drawLowerLayers(input, ladder, layers);
            return Promise.resolve({
                layers: layers as CapturedFrame[],
                index: input.index,
                dropTrace: input.dropTrace,
                rotation: input.rotation,
                stats: input.stats,
            });
        } catch (e) {
            warnLog?.log('spatialize: process failed:', e);
            closeFrames(layers
                .filter((layer): layer is CapturedFrame => layer !== null)
                .map(layer => layer.frame));
            return Promise.reject(e instanceof Error ? e : new Error(String(e)));
        }
    }

    dispose(): void {
        for (const slot of this.slots)
            resizeCanvas(slot.canvas, 0, 0);
        this.slots.length = 0;
    }

    // Releases slots beyond `newLength` (used when the ladder shrank).
    // Keeps lower slots untouched so they reuse their canvas allocation.
    shrinkTo(newLength: number): void {
        if (newLength >= this.slots.length)
            return;
        for (let i = newLength; i < this.slots.length; i++)
            resizeCanvas(this.slots[i].canvas, 0, 0);
        this.slots.length = newLength;
    }

    private drawLowerLayers(
        input: NormalizedFrame,
        ladder: readonly LayerSpec[],
        layers: (CapturedFrame | null)[],
    ): void {
        const painted: number[] = [ladder.length - 1];
        for (let i = ladder.length - 2; i >= 0; i--) {
            const { width, height } = ladder[i];
            const slot = this.prepareSlot(i, width, height);
            const choice = pickSource(i, ladder, painted);
            if (choice.kind === 'higher' && choice.idx < ladder.length - 1) {
                const srcSlot = this.slots[choice.idx];
                slot.ctx.drawImage(
                    srcSlot.canvas,
                    0, 0, srcSlot.canvas.width, srcSlot.canvas.height,
                    0, 0, width, height);
            } else {
                // VideoFrame as CanvasImageSource has intrinsic size =
                // displayWidth/Height (per WebCodecs spec). On Chrome MSTP
                // `crop-and-scale` the coded plane is the camera's native
                // sensor rect while display is the scaled output — sampling
                // with coded coords reads beyond the visible region and
                // pixels past it render black. Fall back to coded only if
                // display is unreported (older WebKit before 17.x).
                const inputW = input.frame.displayWidth || input.frame.codedWidth;
                const inputH = input.frame.displayHeight || input.frame.codedHeight;
                slot.ctx.drawImage(
                    input.frame,
                    0, 0, inputW, inputH,
                    0, 0, width, height);
            }
            layers[i] = makeLayerEnvelope(input, new VideoFrame(slot.canvas, {
                timestamp: input.frame.timestamp,
                alpha: 'discard',
            }));
            painted.push(i);
        }
    }

    private prepareSlot(index: number, width: number, height: number): Slot {
        this.slots[index] ??= createSlot('spatialize');
        const slot = this.slots[index];
        prepareSlot(slot, width, height);
        return slot;
    }
}

export function pickSource(
    targetIdx: number,
    ladder: readonly LayerSpec[],
    paintedHigherIdxs: readonly number[],
): { kind: 'original' } | { kind: 'higher'; idx: number } {
    if (targetIdx >= ladder.length - 1)
        return { kind: 'original' };
    const targetLong = longEdge(ladder[targetIdx]);
    let bestIdx = -1;
    let bestLong = Number.POSITIVE_INFINITY;
    for (const i of paintedHigherIdxs) {
        if (i <= targetIdx) continue;
        const lo = longEdge(ladder[i]);
        if (lo >= 2 * targetLong && lo < bestLong) {
            bestIdx = i;
            bestLong = lo;
        }
    }
    return bestIdx >= 0 ? { kind: 'higher', idx: bestIdx } : { kind: 'original' };
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

function longEdge(spec: LayerSpec): number {
    return Math.max(spec.width, spec.height);
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

function closeFrames(frames: readonly VideoFrame[]): void {
    for (const frame of frames) {
        try { frame.close(); } catch { /* ignore */ }
    }
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

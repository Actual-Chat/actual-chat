import { type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import { DeviceOrientation, normalizeRotationQuarter, type RotationQuarter } from 'orientation';
import type { CapturedBundle, CapturedFrame } from '../frame-envelopes';
import { cameraRotationDeg } from '../orientation/quantize';
import { parallelMap } from './parallel-map';

const { warnLog } = getLogs('VideoPipeline');

export interface LayerSpec {
    width: number;
    height: number;
}

export interface DownscaleOptions {
    // Bottom-first: ladder[0] is the base layer.
    ladder: readonly LayerSpec[];
    isCamera: boolean;
    isFrontCamera: boolean;
    isIos: boolean;
    concurrency?: number;
    // On hang: reset the slot processor, recreate on next frame, force a
    // keyframe so encoders restart cleanly. Default 1500 ms.
    hangTimeoutMs?: number;
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
}

interface SlotState {
    processor: DownscaleSlotProcessor | null;
}

interface ProcessResult {
    frames: VideoFrame[];
    rotation: RotationQuarter;
}

interface FrameTransform {
    cropboxRotation: RotationQuarter;
    wireRotation: RotationQuarter;
}

interface Slot {
    canvas: OffscreenCanvas;
    ctx: OffscreenCanvasRenderingContext2D;
}

// CapturedFrame -> CapturedBundle. Per-layer envelopes share all
// metadata from the input; only `frame` differs. Output is bottom-first.
// Up to `concurrency` source moments are processed in parallel, each on its
// own canvas set; ordering is preserved by parallelMap.
export function downscale(opts: DownscaleOptions): PipeOperator<CapturedFrame, CapturedBundle> {
    if (opts.ladder.length === 0)
        throw new Error('downscale: ladder must contain at least one layer');

    const ladder = opts.ladder.slice();
    const concurrency = opts.concurrency ?? 2;
    const hangTimeoutMs = opts.hangTimeoutMs ?? 1_500;
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms): unknown => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn ?? ((h: unknown): void => clearTimeout(h as ReturnType<typeof setTimeout>));

    // Shared across slots: single-threaded JS makes the read-modify-write
    // sequences atomic between awaits, so post-process() reads are consistent.
    let consecutiveHangs = 0;
    let forceKeyframeAfterHang = false;
    const orientation = new DownscaleOrientation(opts);

    const slotStates: (SlotState | undefined)[] = new Array<SlotState | undefined>(concurrency).fill(undefined);

    return source => {
        const stage = parallelMap<CapturedFrame, CapturedBundle | null>({
            concurrency,
            onSlotInit: slotId => {
                slotStates[slotId] = { processor: null };
            },
            onSlotDispose: slotId => {
                slotStates[slotId]?.processor?.dispose();
                slotStates[slotId] = undefined;
            },
            onUnconsumedResult: bundle => {
                if (bundle) closeBundleLayers(bundle);
            },
            map: async (envelope, slotId) => {
                const slot = slotStates[slotId]!;
                slot.processor ??= new DownscaleSlotProcessor(orientation);

                let result: ProcessResult | null = null;
                let raced: ProcessResult | 'timeout';
                const processPromise = slot.processor.process(envelope.frame, ladder);
                let timerHandle: unknown = null;
                const timeoutP = new Promise<'timeout'>(resolve => {
                    timerHandle = setTimeoutFn(() => resolve('timeout'), hangTimeoutMs);
                });
                try {
                    raced = await Promise.race([processPromise, timeoutP]);
                } finally {
                    if (timerHandle !== null) {
                        try { clearTimeoutFn(timerHandle); } catch { /* ignore */ }
                    }
                }
                if (raced === 'timeout') {
                    consecutiveHangs++;
                    forceKeyframeAfterHang = true;
                    // Detach: tail handler closes anything the stuck process()
                    // eventually produces. Input frame is owned by process().
                    void processPromise.then(
                        produced => { closeFrames(produced.frames); },
                        () => { /* downscale error already logged */ },
                    );
                    slot.processor.dispose();
                    slot.processor = null;
                    if (consecutiveHangs >= 4)
                        throw new Error(
                            `downscale: hang watchdog fired ${consecutiveHangs} times in a row, giving up`);
                    return null;
                }

                consecutiveHangs = 0;
                result = raced;
                if (result.frames.length !== ladder.length) {
                    closeFrames(result.frames);
                    throw new Error(
                        `downscale: processor returned ${result.frames.length} frames, expected ${ladder.length}`);
                }

                const forceKeyframe = envelope.forceKeyframe || forceKeyframeAfterHang;
                forceKeyframeAfterHang = false;
                const layerSource = {
                    ...envelope,
                    forceKeyframe,
                    rotation: result.rotation,
                };
                const layers = result.frames.map(frame => makeLayerEnvelope(layerSource, frame));
                return {
                    layers,
                    index: envelope.index,
                    dropTrace: envelope.dropTrace,
                    rotation: result.rotation,
                    stats: envelope.stats,
                };
            },
        });

        // Skip post-hang null entries (input consumed, no bundle produced).
        const op = stage(source);
        return (async function* (): AsyncIterable<CapturedBundle> {
            for await (const bundle of op) {
                if (bundle !== null) yield bundle;
            }
        })() as unknown as ReturnType<PipeOperator<CapturedFrame, CapturedBundle>>;
    };
}

class DownscaleOrientation {
    private initialDeviceAngle: number | null = null;
    private currentCropboxRotation: RotationQuarter = 0;

    constructor(private readonly opts: DownscaleOptions) {}

    decide(input: VideoFrame, layers: readonly LayerSpec[]): FrameTransform {
        if (!this.opts.isCamera)
            return { cropboxRotation: 0, wireRotation: 0 };
        if (this.opts.isIos)
            return this.decideIosTransform();

        return this.decideChromeStyleTransform(input, layers);
    }

    private decideIosTransform(): FrameTransform {
        const wireRotation = normalizeRotationQuarter(
            cameraRotationDeg(DeviceOrientation.current * 90, this.opts.isFrontCamera) / 90);
        return { cropboxRotation: 0, wireRotation };
    }

    private decideChromeStyleTransform(input: VideoFrame, layers: readonly LayerSpec[]): FrameTransform {
        const cropboxRotation = this.pickCropboxRotation(input, layers);
        return {
            cropboxRotation,
            // Android Chrome/WebView already handles the hidden 180-degree case by
            // keeping/returning the frame upright. For visible +/-90 dimension
            // flips, rotate the receiver by the inverse of the baked pixel turn.
            wireRotation: normalizeRotationQuarter(-cropboxRotation),
        };
    }

    private pickCropboxRotation(input: VideoFrame, layers: readonly LayerSpec[]): RotationQuarter {
        if (layers.length === 0)
            return 0;

        const top = topLayer(layers);
        const sourceIsPortrait = input.codedHeight > input.codedWidth;
        const encoderIsPortrait = top.height > top.width;
        if (sourceIsPortrait === encoderIsPortrait) {
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

class DownscaleSlotProcessor {
    private readonly slots: Slot[] = [];

    constructor(private readonly orientation: DownscaleOrientation) {}

    process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<ProcessResult> {
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        let mustCloseInput = true;
        try {
            this.ensureSlots(layers);
            const transform = this.orientation.decide(input, layers);
            const frames = this.drawLayers(input, layers, transform.cropboxRotation, results);
            mustCloseInput = false;
            try { input.close(); } catch { /* already closed */ }
            return Promise.resolve({ frames, rotation: transform.wireRotation });
        } catch (e) {
            warnLog?.log('downscale: process failed:', e);
            closeFrames(results.filter((r): r is VideoFrame => r !== null));
            if (mustCloseInput)
                try { input.close(); } catch { /* ignore */ }
            return Promise.reject(e instanceof Error ? e : new Error(String(e)));
        }
    }

    dispose(): void {
        this.slots.length = 0;
    }

    private drawLayers(
        input: VideoFrame,
        layers: readonly LayerSpec[],
        cropboxRotation: RotationQuarter,
        results: (VideoFrame | null)[],
    ): VideoFrame[] {
        const painted: number[] = [];
        const inputW = input.codedWidth;
        const inputH = input.codedHeight;
        const rotatedSrcW = (cropboxRotation & 1) === 1 ? inputH : inputW;
        const rotatedSrcH = (cropboxRotation & 1) === 1 ? inputW : inputH;

        for (let i = layers.length - 1; i >= 0; i--) {
            const { width, height } = layers[i];
            const slot = this.prepareSlot(i, width, height);
            const choice = pickSource(i, layers, painted);
            if (choice.kind === 'original') {
                if (cropboxRotation === 0) {
                    const crop = computeCenterCrop(inputW, inputH, width, height);
                    slot.ctx.drawImage(input, crop.sx, crop.sy, crop.sw, crop.sh, 0, 0, width, height);
                } else {
                    drawRotated(
                        slot.ctx, input,
                        inputW, inputH,
                        rotatedSrcW, rotatedSrcH,
                        width, height,
                        cropboxRotation);
                }
            } else {
                const srcSlot = this.slots[choice.idx];
                slot.ctx.drawImage(
                    srcSlot.canvas,
                    0, 0, srcSlot.canvas.width, srcSlot.canvas.height,
                    0, 0, width, height);
            }
            results[i] = new VideoFrame(slot.canvas, {
                timestamp: input.timestamp,
                alpha: 'discard',
            });
            painted.push(i);
        }
        return results as VideoFrame[];
    }

    private ensureSlots(layers: readonly LayerSpec[]): void {
        while (this.slots.length < layers.length)
            this.slots.push(createSlot());
    }

    private prepareSlot(index: number, width: number, height: number): Slot {
        const slot = this.slots[index];
        if (slot.canvas.width !== width)
            slot.canvas.width = width;
        if (slot.canvas.height !== height)
            slot.canvas.height = height;
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

function createSlot(): Slot {
    const canvas = new OffscreenCanvas(0, 0);
    const ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
    if (!ctx)
        throw new Error('downscale: 2D context unavailable on OffscreenCanvas');

    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    return { canvas, ctx };
}

function longEdge(spec: LayerSpec): number {
    return Math.max(spec.width, spec.height);
}

function topLayer(layers: readonly LayerSpec[]): LayerSpec {
    return layers[layers.length - 1];
}

function signedAngleDeltaDeg(from: number, to: number): number {
    if (!Number.isFinite(from) || !Number.isFinite(to))
        return 0;
    return ((((to - from + 180) % 360) + 360) % 360) - 180;
}

function computeCenterCrop(
    sx: number, sy: number,
    dx: number, dy: number,
): { sx: number; sy: number; sw: number; sh: number } {
    if (sx <= 0 || sy <= 0 || dx <= 0 || dy <= 0)
        return { sx: 0, sy: 0, sw: sx, sh: sy };

    const srcAspect = sx / sy;
    const dstAspect = dx / dy;
    if (Math.abs(srcAspect - dstAspect) < 1e-3)
        return { sx: 0, sy: 0, sw: sx, sh: sy };

    if (srcAspect > dstAspect) {
        const sw = Math.round(sy * dstAspect);
        return { sx: Math.floor((sx - sw) / 2), sy: 0, sw, sh: sy };
    }
    const sh = Math.round(sx / dstAspect);
    return { sx: 0, sy: Math.floor((sy - sh) / 2), sw: sx, sh };
}

function drawRotated(
    ctx: OffscreenCanvasRenderingContext2D,
    input: VideoFrame,
    inputW: number, inputH: number,
    rotatedSrcW: number, rotatedSrcH: number,
    targetW: number, targetH: number,
    rotation: RotationQuarter,
): void {
    const cropR = computeCenterCrop(rotatedSrcW, rotatedSrcH, targetW, targetH);
    let sx = 0, sy = 0, sw = 0, sh = 0;
    switch (rotation) {
    case 1: // 90deg CW
        sx = cropR.sy;
        sy = inputH - cropR.sx - cropR.sw;
        sw = cropR.sh;
        sh = cropR.sw;
        break;
    case 2: // 180deg
        sx = inputW - cropR.sx - cropR.sw;
        sy = inputH - cropR.sy - cropR.sh;
        sw = cropR.sw;
        sh = cropR.sh;
        break;
    case 3: // 270deg CW
        sx = inputW - cropR.sy - cropR.sh;
        sy = cropR.sx;
        sw = cropR.sh;
        sh = cropR.sw;
        break;
    default:
        sx = cropR.sx; sy = cropR.sy; sw = cropR.sw; sh = cropR.sh;
    }

    ctx.save();
    ctx.translate(targetW / 2, targetH / 2);
    ctx.rotate((rotation * Math.PI) / 2);
    const swap = (rotation & 1) === 1;
    const dw = swap ? targetH : targetW;
    const dh = swap ? targetW : targetH;
    ctx.drawImage(input, sx, sy, sw, sh, -dw / 2, -dh / 2, dw, dh);
    ctx.restore();
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

function makeLayerEnvelope(source: CapturedFrame, frame: VideoFrame): CapturedFrame {
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

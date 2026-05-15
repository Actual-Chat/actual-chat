import { type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import { DeviceOrientation, normalizeRotationQuarter, type RotationQuarter } from 'orientation';
import type { CapturedBundle, CapturedFrame, NormalizedFrame } from '../frame-envelopes';
import { cameraRotationDeg } from '../orientation/quantize';
import { drawFrameCover, resizeCanvas } from '../canvas/resize';
import { parallelMap } from './parallel-map';

const { warnLog } = getLogs('VideoPipeline');

export interface LayerSpec {
    width: number;
    height: number;
}

export interface NormalizeFrameOptions {
    target: LayerSpec;
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

export interface SpatializeOptions {
    // Bottom-first: ladder[0] is the base layer. The top layer must match the
    // normalized frame dimensions.
    ladder: readonly LayerSpec[];
    concurrency?: number;
}

interface NormalizeSlotState {
    processor: NormalizeFrameSlotProcessor | null;
}

interface SpatialSlotState {
    processor: SpatializeSlotProcessor | null;
}

interface NormalizeProcessResult {
    frame: VideoFrame;
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

// CapturedFrame -> NormalizedFrame. This is the sender surface: one frame at
// the top spatial layer's locked dimensions, with crop/orientation fixes baked
// into pixels and the corresponding wire rotation set on the envelope.
export function normalizeFrame(opts: NormalizeFrameOptions): PipeOperator<CapturedFrame, NormalizedFrame> {
    const target = { ...opts.target };
    const concurrency = opts.concurrency ?? 2;
    const hangTimeoutMs = opts.hangTimeoutMs ?? 1_500;
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms): unknown => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn ?? ((h: unknown): void => clearTimeout(h as ReturnType<typeof setTimeout>));

    let consecutiveHangs = 0;
    let forceKeyframeAfterHang = false;
    const orientation = new NormalizeFrameOrientation(opts);
    const slotStates: (NormalizeSlotState | undefined)[] = new Array<NormalizeSlotState | undefined>(concurrency).fill(undefined);

    return source => {
        const stage = parallelMap<CapturedFrame, NormalizedFrame | null>({
            concurrency,
            onSlotInit: slotId => {
                slotStates[slotId] = { processor: null };
            },
            onSlotDispose: slotId => {
                slotStates[slotId]?.processor?.dispose();
                slotStates[slotId] = undefined;
            },
            onUnconsumedResult: frame => {
                if (frame)
                    try { frame.frame.close(); } catch { /* ignore */ }
            },
            map: async (envelope, slotId) => {
                const slot = slotStates[slotId]!;
                slot.processor ??= new NormalizeFrameSlotProcessor(orientation);

                let result: NormalizeProcessResult | null = null;
                let raced: NormalizeProcessResult | 'timeout';
                const processPromise = slot.processor.process(envelope.frame, target);
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
                        produced => { try { produced.frame.close(); } catch { /* ignore */ } },
                        () => { /* normalize error already logged */ },
                    );
                    slot.processor.dispose();
                    slot.processor = null;
                    if (consecutiveHangs >= 4)
                        throw new Error(
                            `normalizeFrame: hang watchdog fired ${consecutiveHangs} times in a row, giving up`);
                    return null;
                }

                consecutiveHangs = 0;
                result = raced;
                const forceKeyframe = envelope.forceKeyframe || forceKeyframeAfterHang;
                forceKeyframeAfterHang = false;
                return {
                    ...envelope,
                    frame: result.frame,
                    forceKeyframe,
                    rotation: result.rotation,
                };
            },
        });

        const op = stage(source);
        return (async function* (): AsyncIterable<NormalizedFrame> {
            for await (const frame of op) {
                if (frame !== null) yield frame;
            }
        })() as unknown as ReturnType<PipeOperator<CapturedFrame, NormalizedFrame>>;
    };
}

// NormalizedFrame -> CapturedBundle. Adds any lower spatial layers by scaling
// from the already-normalized top layer. Output remains bottom-first.
export function spatialize(opts: SpatializeOptions): PipeOperator<NormalizedFrame, CapturedBundle> {
    if (opts.ladder.length === 0)
        throw new Error('spatialize: ladder must contain at least one layer');

    const ladder = opts.ladder.slice();
    const concurrency = opts.concurrency ?? 2;
    const slotStates: (SpatialSlotState | undefined)[] = new Array<SpatialSlotState | undefined>(concurrency).fill(undefined);

    return source => {
        const stage = parallelMap<NormalizedFrame, CapturedBundle>({
            concurrency,
            onSlotInit: slotId => {
                slotStates[slotId] = { processor: null };
            },
            onSlotDispose: slotId => {
                slotStates[slotId]?.processor?.dispose();
                slotStates[slotId] = undefined;
            },
            onUnconsumedResult: bundle => closeBundleLayers(bundle),
            map: async (frame, slotId) => {
                const slot = slotStates[slotId]!;
                slot.processor ??= new SpatializeSlotProcessor();
                return slot.processor.process(frame, ladder);
            },
        });

        return stage(source) as unknown as ReturnType<PipeOperator<NormalizedFrame, CapturedBundle>>;
    };
}

class NormalizeFrameOrientation {
    private initialDeviceAngle: number | null = null;
    private currentCropboxRotation: RotationQuarter = 0;

    constructor(private readonly opts: NormalizeFrameOptions) {}

    decide(input: VideoFrame, target: LayerSpec): FrameTransform {
        if (!this.opts.isCamera)
            return { cropboxRotation: 0, wireRotation: 0 };

        return this.opts.isIos
            ? this.decideIosTransform()
            : this.decideChromeStyleTransform(input, target);
    }

    private decideIosTransform(): FrameTransform {
        const wireRotation = normalizeRotationQuarter(
            cameraRotationDeg(DeviceOrientation.quarter * 90, this.opts.isFrontCamera) / 90);
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

class NormalizeFrameSlotProcessor {
    private readonly slot = createSlot('normalizeFrame');

    constructor(private readonly orientation: NormalizeFrameOrientation) {}

    process(input: VideoFrame, target: LayerSpec): Promise<NormalizeProcessResult> {
        let result: VideoFrame | null = null;
        let mustCloseInput = true;
        try {
            const transform = this.orientation.decide(input, target);
            this.drawNormalized(input, target, transform.cropboxRotation);
            result = new VideoFrame(this.slot.canvas, {
                timestamp: input.timestamp,
                alpha: 'discard',
            });
            mustCloseInput = false;
            try { input.close(); } catch { /* already closed */ }
            return Promise.resolve({ frame: result, rotation: transform.wireRotation });
        } catch (e) {
            warnLog?.log('normalizeFrame: process failed:', e);
            if (result)
                try { result.close(); } catch { /* ignore */ }
            if (mustCloseInput)
                try { input.close(); } catch { /* ignore */ }
            return Promise.reject(e instanceof Error ? e : new Error(String(e)));
        }
    }

    dispose(): void {
        this.slot.canvas.width = 0;
        this.slot.canvas.height = 0;
    }

    private drawNormalized(
        input: VideoFrame,
        target: LayerSpec,
        cropboxRotation: RotationQuarter,
    ): void {
        const { width, height } = target;
        prepareSlot(this.slot, width, height);
        drawFrameCover(this.slot.ctx, input, width, height, cropboxRotation);
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
                slot.ctx.drawImage(
                    input.frame,
                    0, 0, input.frame.codedWidth, input.frame.codedHeight,
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

function closeBundleLayers(bundle: CapturedBundle): void {
    for (const layer of bundle.layers) {
        try { layer.frame.close(); } catch { /* ignore */ }
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

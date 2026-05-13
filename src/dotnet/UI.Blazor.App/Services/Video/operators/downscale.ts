import { type PipeOperator } from 'ix-ext';
import type { CapturedBundle, CapturedFrame } from '../frame-envelopes';
import { parallelMap } from './parallel-map';

export interface LayerSpec {
    width: number;
    height: number;
}

// Contract: one frame per spec in order; implementation owns the input
// frame (consumes/closes it, including on failure before throwing).
export interface DownscalerLike {
    process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]>;
    dispose?(): void;
}

export interface DownscaleOptions {
    // Bottom-first: ladder[0] is the base layer.
    ladder: readonly LayerSpec[];
    // Lazy-init per slot so construction can run before the GPU device exists.
    createDownscaler: () => DownscalerLike;
    concurrency?: number;
    // On hang: close the downscaler, recreate on next frame, force a
    // keyframe so encoders restart cleanly. Default 1500 ms.
    hangTimeoutMs?: number;
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
}

interface SlotState {
    downscaler: DownscalerLike | null;
}

// CapturedFrame -> CapturedBundle. Per-layer envelopes share all
// metadata from the input — only `frame` differs. Output bottom-first.
// Up to `concurrency` bundles run process() in parallel, each on its
// own downscaler instance; ordering preserved via parallelMap. Output
// ownership flips at yield; before yield the operator closes returned
// frames (input frame is the downscaler's responsibility).
export function downscale(opts: DownscaleOptions): PipeOperator<CapturedFrame, CapturedBundle> {
    if (opts.ladder.length === 0)
        throw new Error('downscale: ladder must contain at least one layer');
    const ladder = opts.ladder.slice();
    const createDownscaler = opts.createDownscaler;
    const concurrency = opts.concurrency ?? 2;
    const hangTimeoutMs = opts.hangTimeoutMs ?? 1_500;
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms): unknown => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn ?? ((h: unknown): void => clearTimeout(h as ReturnType<typeof setTimeout>));

    // Shared across slots: single-threaded JS makes the read-modify-write
    // sequences atomic between awaits, so post-process() reads are consistent.
    let consecutiveHangs = 0;
    let forceKeyframeAfterHang = false;

    const slotStates: (SlotState | undefined)[] = new Array<SlotState | undefined>(concurrency).fill(undefined);

    return source => {
        const stage = parallelMap<CapturedFrame, CapturedBundle | null>({
            concurrency,
            onSlotInit: slotId => {
                slotStates[slotId] = { downscaler: null };
            },
            onSlotDispose: slotId => {
                const s = slotStates[slotId];
                if (s?.downscaler && typeof s.downscaler.dispose === 'function') {
                    try { s.downscaler.dispose(); } catch { /* ignore */ }
                }
                slotStates[slotId] = undefined;
            },
            onUnconsumedResult: bundle => {
                if (bundle) closeBundleLayers(bundle);
            },
            map: async (envelope, slotId) => {
                const slot = slotStates[slotId]!;
                slot.downscaler ??= createDownscaler();
                const downscaler = slot.downscaler;

                let frames: VideoFrame[] | null = null;
                let timedOut = false;
                let raced: VideoFrame[] | 'timeout';
                const processPromise = downscaler.process(envelope.frame, ladder);
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
                    timedOut = true;
                    consecutiveHangs++;
                    forceKeyframeAfterHang = true;
                    // Detach: tail handler closes anything the stuck process()
                    // eventually produces. Input frame is owned by process().
                    void processPromise.then(
                        produced => { closeFrames(produced); },
                        () => { /* downscaler error already logged */ },
                    );
                    const stuck = slot.downscaler;
                    slot.downscaler = null;
                    if (typeof stuck.dispose === 'function') {
                        try { stuck.dispose(); } catch { /* ignore */ }
                    }
                    if (consecutiveHangs >= 4)
                        throw new Error(
                            `downscale: hang watchdog fired ${consecutiveHangs} times in a row, giving up`);
                    return null;
                }
                consecutiveHangs = 0;
                frames = raced;
                if (frames.length !== ladder.length) {
                    closeFrames(frames);
                    throw new Error(
                        `downscale: downscaler returned ${frames.length} frames, expected ${ladder.length}`);
                }

                // After a hang, force a keyframe so encoders re-anchor.
                const layerSource = forceKeyframeAfterHang
                    ? { ...envelope, forceKeyframe: true }
                    : envelope;
                forceKeyframeAfterHang = false;
                const layers: CapturedFrame[] = [];
                for (const frame of frames) {
                    layers.push(makeLayerEnvelope(layerSource, frame));
                }
                void timedOut;
                return {
                    layers,
                    index: envelope.index,
                    dropTrace: envelope.dropTrace,
                    rotation: envelope.rotation,
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
        // Per-layer envelope shares the bundle's drop trace by reference; the
        // bundle is the unit of drop accounting in the post-downscale pipeline.
        dropTrace: source.dropTrace,
        sourceWidth: source.sourceWidth,
        sourceHeight: source.sourceHeight,
        forceKeyframe: source.forceKeyframe,
        rotation: source.rotation,
        stats: source.stats,
    };
}

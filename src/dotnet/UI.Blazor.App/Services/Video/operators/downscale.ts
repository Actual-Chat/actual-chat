import { type PipeOperator } from 'ix-ext';
import type { CapturedBundle, CapturedFrame } from '../frame-envelopes';
import { parallelMap } from './parallel-map';

// Output dims for one layer (encoder dims for that layer).
export interface LayerSpec {
    width: number;
    height: number;
}

/**
 * Production binding: `WebGpuDownscaler` (one render pass per
 * non-identity slot). Tests can inject a pure clone-based fake.
 *
 * Contract:
 * - One frame per spec, same order.
 * - Implementation takes the input frame (consumes/closes it).
 * - On failure, implementation closes `input` itself before throwing
 *   (the operator does not double-close).
 */
export interface DownscalerLike {
    process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]>;
    /** GPU-resource disposal called from the operator's `finally`. */
    dispose?(): void;
}

export interface DownscaleOptions {
    /** Bottom-first ladder. `ladder[0]` is the base (lowest-resolution)
     *  layer; the last entry is the top layer. */
    ladder: readonly LayerSpec[];
    /** Lazy-init: called per slot on first dispatch so construction can
     *  happen before the GPU device exists. With concurrency > 1 the
     *  factory is invoked once per slot. */
    createDownscaler: () => DownscalerLike;
    /** Max concurrent process() calls — i.e. how many bundles can be
     *  in-flight through the downscale stage at once. Each slot owns its
     *  own downscaler instance. Default: 2. */
    concurrency?: number;
    /** Max time `downscaler.process()` is allowed to take before the
     *  operator considers the GPU wedged: closes the downscaler,
     *  recreates it on the next frame, marks the next emitted bundle's
     *  forceKeyframe so encoders restart cleanly. Default 1500 ms. */
    hangTimeoutMs?: number;
    /** Test seam for setTimeout. */
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
}

interface SlotState {
    downscaler: DownscalerLike | null;
}

/**
 * `CapturedFrame → CapturedBundle`. Per-layer envelopes inside the
 * bundle share all metadata from the input (capturedAt, index,
 * forceKeyframe, stats, sourceWidth/Height) — only `frame` differs.
 * Output is bottom-first: `layers[0]` = base layer, `layers[length-1]`
 * = top layer.
 *
 * Concurrency: up to `concurrency` bundles run through `process()`
 * simultaneously, each on its own downscaler instance. Output ordering
 * matches source order via `parallelMap`.
 *
 * Output ownership flips to downstream at `yield`. On the failure path
 * before yield, the operator closes any returned frames; the input
 * frame's close is the downscaler's responsibility per its contract.
 */
export function downscale(opts: DownscaleOptions): PipeOperator<CapturedFrame, CapturedBundle> {
    if (opts.ladder.length === 0)
        throw new Error('downscale: ladder must contain at least one layer');
    const ladder = opts.ladder.slice();
    const createDownscaler = opts.createDownscaler;
    const concurrency = opts.concurrency ?? 2;
    const hangTimeoutMs = opts.hangTimeoutMs ?? 1_500;
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms): unknown => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn ?? ((h: unknown): void => clearTimeout(h as ReturnType<typeof setTimeout>));

    // Shared across slots: hang accounting + post-hang forceKeyframe
    // latch. With single-threaded JS, the read-modify-write sequences in
    // the slot map function are atomic w.r.t. each other; only awaits
    // interleave, so reading these flags AFTER process() returns gives a
    // consistent post-hang view.
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
                    // Detach: any frames the stuck process() eventually
                    // produces will be closed via this tail handler. The
                    // input frame is owned by process() per its contract.
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
                    // Skip this bundle. parallelMap yields nothing for
                    // null filter below.
                    return null;
                }
                consecutiveHangs = 0;
                frames = raced;
                if (frames.length !== ladder.length) {
                    closeFrames(frames);
                    throw new Error(
                        `downscale: downscaler returned ${frames.length} frames, expected ${ladder.length}`);
                }

                // Bottom-first: bundle.layers[0] = base, .layers[topIdx] = top.
                // After a hang, mark forceKeyframe so the encoders re-anchor
                // at the next clean bundle.
                const layerSource = forceKeyframeAfterHang
                    ? { ...envelope, forceKeyframe: true }
                    : envelope;
                forceKeyframeAfterHang = false;
                const layers: CapturedFrame[] = [];
                for (const frame of frames) {
                    layers.push(makeLayerEnvelope(layerSource, frame));
                }
                // Reference void so the linter doesn't flag the early-fail
                // branch helper as unused after we move ownership to the
                // returned bundle.
                void timedOut;
                return {
                    layers,
                    stats: envelope.stats,
                };
            },
        });

        // Skip post-hang null entries (those are the "input frame
        // consumed but no bundle produced" case).
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
        sourceWidth: source.sourceWidth,
        sourceHeight: source.sourceHeight,
        forceKeyframe: source.forceKeyframe,
        stats: source.stats,
    };
}

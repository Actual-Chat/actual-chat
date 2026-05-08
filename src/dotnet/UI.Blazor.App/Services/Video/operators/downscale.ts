import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame, SimulcastBundle } from '../frame-envelopes';

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
    /** Bottom-first ladder. Last entry becomes `SimulcastBundle.primary`. */
    ladder: readonly LayerSpec[];
    /** Lazy-init: called once on first iteration so construction can
     *  happen before the GPU device exists. */
    createDownscaler: () => DownscalerLike;
    /** Max time `downscaler.process()` is allowed to take before the
     *  operator considers the GPU wedged: closes the downscaler,
     *  recreates it on the next frame, marks the input frame's
     *  forceKeyframe so encoders restart cleanly. Default 1500 ms. */
    hangTimeoutMs?: number;
    /** Test seam for setTimeout. */
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
}

/**
 * `CapturedFrame → SimulcastBundle`. Per-layer envelopes share all
 * metadata from the input (capturedAt, index, forceKeyframe, stats,
 * sourceWidth/Height) — only `frame` differs.
 *
 * Output ownership flips to downstream at `yield`. On the failure path
 * before yield, the operator closes any returned frames; the input
 * frame's close is the downscaler's responsibility per its contract.
 */
export function downscale(opts: DownscaleOptions): PipeOperator<CapturedFrame, SimulcastBundle> {
    if (opts.ladder.length === 0)
        throw new Error('downscale: ladder must contain at least one layer');
    const ladder = opts.ladder.slice();
    const createDownscaler = opts.createDownscaler;
    const topIdx = ladder.length - 1;
    const hangTimeoutMs = opts.hangTimeoutMs ?? 1_500;
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms): unknown => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn ?? ((h: unknown): void => clearTimeout(h as ReturnType<typeof setTimeout>));

    return source => from(downscaleAsync(
        source, ladder, topIdx, createDownscaler,
        hangTimeoutMs, setTimeoutFn, clearTimeoutFn));
}

async function* downscaleAsync(
    source: AsyncIterable<CapturedFrame>,
    ladder: readonly LayerSpec[],
    topIdx: number,
    createDownscaler: () => DownscalerLike,
    hangTimeoutMs: number,
    setTimeoutFn: (cb: () => void, ms: number) => unknown,
    clearTimeoutFn: (handle: unknown) => void,
): AsyncIterable<SimulcastBundle> {
    let downscaler: DownscalerLike | null = null;
    let consecutiveHangs = 0;
    // Set after a hang; cleared once the post-hang bundle is yielded with
    // forceKeyframe=true. Encoders downstream consume this flag and
    // restart at a clean keyframe boundary.
    let forceKeyframeAfterHang = false;
    try {
        for await (const envelope of source) {
            let frames: VideoFrame[] | null = null;
            let mustClose = true;
            let timedOut = false;
            try {
                downscaler ??= createDownscaler();
                // Race process() against a hang timeout. On timeout the
                // downscaler's process() owns the input frame per its
                // contract — we cannot reach in to close it. Drop the
                // downscaler reference so the next iteration creates a
                // fresh one; the next bundle is force-keyframed so the
                // encoder chain re-anchors.
                const processPromise = downscaler.process(envelope.frame, ladder);
                let timerHandle: unknown = null;
                const timeoutP = new Promise<'timeout'>(resolve => {
                    timerHandle = setTimeoutFn(() => resolve('timeout'), hangTimeoutMs);
                });
                let raced: VideoFrame[] | 'timeout';
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
                    // produces will be closed via the tail handler below
                    // when the promise finally settles.
                    void processPromise.then(
                        produced => { closeFrames(produced); },
                        () => { /* downscaler error already logged */ },
                    );
                    const stuck = downscaler;
                    downscaler = null;
                    if (stuck && typeof stuck.dispose === 'function') {
                        try { stuck.dispose(); } catch { /* ignore */ }
                    }
                    if (consecutiveHangs >= 4)
                        throw new Error(
                            `downscale: hang watchdog fired ${consecutiveHangs} times in a row, giving up`);
                    // Input frame ownership: the original process() call
                    // already took ownership per the contract. Don't
                    // double-close.
                    mustClose = false;
                    continue;
                }
                consecutiveHangs = 0;
                frames = raced;
                if (frames.length !== ladder.length) {
                    throw new Error(
                        `downscale: downscaler returned ${frames.length} frames, expected ${ladder.length}`);
                }

                // primary = top tier (frames[topIdx]); extras = bottom-up
                // [base..second-highest]. After a hang, mark forceKeyframe
                // so the encoders re-anchor at the next clean bundle.
                const layerSource = forceKeyframeAfterHang
                    ? { ...envelope, forceKeyframe: true }
                    : envelope;
                forceKeyframeAfterHang = false;
                const primary = makeLayerEnvelope(layerSource, frames[topIdx]);
                const extras: CapturedFrame[] = [];
                for (let i = 0; i < topIdx; i++) {
                    extras.push(makeLayerEnvelope(layerSource, frames[i]));
                }
                const bundle: SimulcastBundle = {
                    primary,
                    extras,
                    stats: envelope.stats,
                };
                frames = null;
                mustClose = false;
                yield bundle;
            } finally {
                if (frames)
                    closeFrames(frames);
                if (mustClose && !timedOut)
                    try { envelope.frame.close(); } catch { /* ignore */ }
            }
        }
    } finally {
        if (downscaler && typeof downscaler.dispose === 'function') {
            try { downscaler.dispose(); } catch { /* ignore */ }
        }
    }
}

function closeFrames(frames: readonly VideoFrame[]): void {
    for (const frame of frames) {
        try { frame.close(); } catch { /* ignore */ }
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

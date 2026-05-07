import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame, SimulcastBundle } from '../frame-envelopes';

// Output dims for one spatial layer (encoder dims for that layer).
export interface SpatialLayerSpec {
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
    process(input: VideoFrame, layers: readonly SpatialLayerSpec[]): Promise<VideoFrame[]>;
    /** GPU-resource disposal called from the operator's `finally`. */
    dispose?(): void;
}

export interface DownscaleOptions {
    /** Bottom-first ladder. Last entry becomes `SimulcastBundle.primary`. */
    ladder: readonly SpatialLayerSpec[];
    /** Lazy-init: called once on first iteration so construction can
     *  happen before the GPU device exists. */
    createDownscaler: () => DownscalerLike;
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

    return source => from(downscaleAsync(source, ladder, topIdx, createDownscaler));
}

async function* downscaleAsync(
    source: AsyncIterable<CapturedFrame>,
    ladder: readonly SpatialLayerSpec[],
    topIdx: number,
    createDownscaler: () => DownscalerLike,
): AsyncIterable<SimulcastBundle> {
    let downscaler: DownscalerLike | null = null;
    try {
        for await (const envelope of source) {
            let frames: VideoFrame[] | null = null;
            let mustClose = true;
            try {
                downscaler ??= createDownscaler();
                frames = await downscaler.process(envelope.frame, ladder);
                if (frames.length !== ladder.length) {
                    throw new Error(
                        `downscale: downscaler returned ${frames.length} frames, expected ${ladder.length}`);
                }

                // primary = top tier (frames[topIdx]); extras = bottom-up
                // [base..second-highest].
                const primary = makeLayerEnvelope(envelope, frames[topIdx]);
                const extras: CapturedFrame[] = [];
                for (let i = 0; i < topIdx; i++) {
                    extras.push(makeLayerEnvelope(envelope, frames[i]));
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
                if (mustClose)
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

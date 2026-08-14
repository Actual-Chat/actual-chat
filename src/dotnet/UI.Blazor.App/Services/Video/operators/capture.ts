import { AsyncIterableX, exclusive, finalize, from, fromReadable } from 'ix-ext';
import type { CapturedFrame, RecorderStats } from '../frame-envelopes';

// Minimal MediaStreamTrackProcessor surface. Tests inject a fake.
interface MstpLike {
    readable: ReadableStream<VideoFrame>;
}

export interface MstpSourceOptions {
    track: MediaStreamTrack;
    stats: RecorderStats;
    abortSignal?: AbortSignal;
    // Graceful stop: ends the sequence without aborting.
    stopSignal?: AbortSignal;
    createProcessor?: (track: MediaStreamTrack) => MstpLike;
}

// Fallback when neither the frame nor the track declares a rate. Whole
// microseconds: the wire encodes duration as 100-ns ticks via BigInt, which
// rejects a fractional value.
const DEFAULT_FRAME_DURATION_US = Math.round(1_000_000 / 30);

// Nominal duration of one source frame, best source first: the frame's own
// duration (WebKit leaves it unset, Chromium supplies it), then the track's
// declared frameRate, then a default. Resolved per frame so a mid-stream
// frameRate change is picked up.
function resolveDurationUs(frame: VideoFrame, track: MediaStreamTrack): number {
    if (frame.duration != null && frame.duration > 0)
        return frame.duration;

    // Guarded, not optional-chained: the type says getSettings is always there, but
    // the tests' fake track is `{} as MediaStreamTrack`.
    const fps = typeof track.getSettings === 'function' ? track.getSettings().frameRate : undefined;
    if (fps != null && fps > 0)
        return Math.round(1_000_000 / fps);

    return DEFAULT_FRAME_DURATION_US;
}

// MediaStreamTrack -> CapturedFrame. No queueing — production backs the
// readable with a single-slot push so slow downstream drops older frames
// at the source.
export function mstpSource(opts: MstpSourceOptions): AsyncIterableX<CapturedFrame> {
    const { track, stats, abortSignal, stopSignal } = opts;
    const createProcessor = opts.createProcessor
        ?? ((t: MediaStreamTrack) => new (globalThis as unknown as {
            MediaStreamTrackProcessor: new (init: { track: MediaStreamTrack }) => MstpLike;
        }).MediaStreamTrackProcessor({ track: t }));
    let cancelSource: (() => void) | null = null;

    // eslint-disable-next-line @typescript-eslint/require-await -- `finalize` expects a Promise; ours is sync.
    const release = async (): Promise<void> => {
        const c = cancelSource;
        cancelSource = null;
        try { c?.(); } catch { /* ignore */ }
    };

    const segment = from(impl());
    return exclusive(finalize<CapturedFrame>(release)(segment));

    async function* impl(): AsyncIterable<CapturedFrame> {
        if (stopSignal?.aborted || abortSignal?.aborted)
            return;

        const processor = createProcessor(track);
        const readCancelController = new AbortController();
        const cancelRead = (): void => {
            if (!readCancelController.signal.aborted)
                readCancelController.abort(stopSignal?.reason ?? abortSignal?.reason);
        };
        stopSignal?.addEventListener('abort', cancelRead, { once: true });
        abortSignal?.addEventListener('abort', cancelRead, { once: true });
        if (stopSignal?.aborted || abortSignal?.aborted)
            cancelRead();
        const rawSource = fromReadable(processor.readable, {
            closeOnReturn: true,
            abortSignal: readCancelController.signal,
        });
        let cancelled = false;
        cancelSource = () => { cancelled = true; };
        // Assigned at capture so that drops at later stages (flood gate, stamp,
        // downscale, encode, wire) appear as gaps in the index downstream.
        let nextIndex = 0;
        try {
            for await (const value of rawSource) {
                let mustClose = true;
                try {
                    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- both flags flip on async paths above.
                    if (cancelled || stopSignal?.aborted || abortSignal?.aborted)
                        return;

                    const envelope: CapturedFrame = {
                        frame: value,
                        capturedAt: { timeMs: 0, epoch: 0 },
                        durationUs: resolveDurationUs(value, track),
                        index: nextIndex++,
                        dropTrace: [],
                        sourceWidth: 0,
                        sourceHeight: 0,
                        forceKeyframe: false,
                        rotation: 0,
                        stats,
                    };
                    stats.framesCaptured++;
                    mustClose = false;
                    yield envelope;
                } finally {
                    if (mustClose)
                        try { value.close(); } catch { /* ignore */ }
                }
            }
        } finally {
            stopSignal?.removeEventListener('abort', cancelRead);
            abortSignal?.removeEventListener('abort', cancelRead);
            cancelSource = null;
        }
    }
}

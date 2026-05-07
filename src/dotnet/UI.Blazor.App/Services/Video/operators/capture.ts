import { AsyncIterableX, exclusive, finalize, from, fromReadable } from 'ix-ext';
import type { CapturedFrame, VideoRecordingStats } from '../frame-envelopes';

// Minimal `MediaStreamTrackProcessor` surface. Tests inject a fake.
interface MstpLike {
    readable: ReadableStream<VideoFrame>;
}

export interface MstpSourceOptions {
    track: MediaStreamTrack;
    stats: VideoRecordingStats;
    abortSignal?: AbortSignal;
    /** Graceful source-completion signal. Unlike abortSignal, this is
     *  the normal stop path and simply ends the captured frame sequence. */
    stopSignal?: AbortSignal;
    createProcessor?: (track: MediaStreamTrack) => MstpLike;
}

// `MediaStreamTrack → CapturedFrame`. No queueing here — production
// backs the readable with a single-slot push (`ReplaceableSlot`) so
// slow downstream naturally drops older frames at the source.
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
        try {
            for await (const value of rawSource) {
                let mustClose = true;
                try {
                    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- both flags flip on async paths above.
                    if (cancelled || stopSignal?.aborted || abortSignal?.aborted)
                        return;

                    stats.framesCaptured++;
                    const envelope: CapturedFrame = {
                        frame: value,
                        capturedAt: { timeMs: 0, epoch: 0 },
                        index: 0,
                        sourceWidth: 0,
                        sourceHeight: 0,
                        forceKeyframe: false,
                        stats,
                    };
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

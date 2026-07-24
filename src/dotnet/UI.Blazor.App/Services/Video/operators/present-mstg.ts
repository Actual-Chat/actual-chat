import { type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import { type DecodedFrame, type PlayerStats } from '../frame-envelopes';
import { presentPacer, type PresentSink } from '../playback/present-pacer';

const { warnLog } = getLogs('VideoPipeline');

export interface MstgPresentOptions {
    getWriter: () => WritableStreamDefaultWriter<VideoFrame>;
    getBufferSpanMs: () => number;
    targetSpanMs: number;
    nowFn?: () => number;
    delayFn?: (ms: number) => Promise<void>;
    holdMs?: number;
    getAudioCaptureOffsetMs?: () => number | null;
    stats?: PlayerStats;
}

export function mstgPresent(opts: MstgPresentOptions): PipeOperator<DecodedFrame, void> {
    return presentPacer({
        getBufferSpanMs: opts.getBufferSpanMs,
        targetSpanMs: opts.targetSpanMs,
        nowFn: opts.nowFn,
        delayFn: opts.delayFn,
        holdMs: opts.holdMs,
        getAudioCaptureOffsetMs: opts.getAudioCaptureOffsetMs,
        createSink: (): PresentSink => {
            const writer = opts.getWriter();
            return {
                async present(frame: VideoFrame): Promise<boolean> {
                    try {
                        // Backpressure: only block when the generator's queue is
                        // actually full (desiredSize is synchronous, so steady state
                        // stays timer-free). When the consumer (<video>/compositor)
                        // is behind, await ready — this paces us to its drain rate
                        // and, on a sustained stall, stalls the present loop + the
                        // upstream decode until it resumes.
                        if ((writer.desiredSize ?? 1) <= 0) {
                            if (opts.stats) opts.stats.presentState = 'mstg:awaiting-ready';
                            await writer.ready;
                        }
                        if (opts.stats) opts.stats.presentState = 'mstg:writing';
                        await writer.write(frame);
                        return true;
                    } catch (e: unknown) {
                        warnLog?.log('mstgPresent: write failed', e);
                        throw e;
                    }
                },
                dispose(): void {
                    try { writer.releaseLock(); } catch { /* ignore */ }
                },
            };
        },
    });
}

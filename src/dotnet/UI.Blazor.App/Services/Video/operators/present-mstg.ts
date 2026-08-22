import { type PipeOperator } from 'ix-ext';
import { TimeoutError, withTimeout } from 'actuallab-core';
import { getLogs } from 'logging';
import { type DecodedFrame, type PlayerStats } from '../frame-envelopes';
import { presentPacer, type PresentSink } from '../playback/present-pacer';

const { warnLog } = getLogs('VideoPipeline');

// Ceiling on one backpressure wait. Normal pacing resolves within a frame
// interval; past this the consumer is wedged rather than merely behind.
const WRITER_READY_TIMEOUT_MS = 1000;

export interface MstgPresentOptions {
    getWriter: () => WritableStreamDefaultWriter<VideoFrame>;
    getBufferSpanMs: () => number;
    targetSpanMs: number;
    nowFn?: () => number;
    delayFn?: (ms: number) => Promise<void>;
    holdMs?: number;
    getAudioCaptureOffsetMs?: () => number | null;
    stats?: PlayerStats;
    abortSignal?: AbortSignal;
}

export function mstgPresent(opts: MstgPresentOptions): PipeOperator<DecodedFrame, void> {
    return presentPacer({
        getBufferSpanMs: opts.getBufferSpanMs,
        targetSpanMs: opts.targetSpanMs,
        nowFn: opts.nowFn,
        delayFn: opts.delayFn,
        holdMs: opts.holdMs,
        getAudioCaptureOffsetMs: opts.getAudioCaptureOffsetMs,
        abortSignal: opts.abortSignal,
        createSink: (): PresentSink => {
            const writer = opts.getWriter();
            return {
                async present(frame: VideoFrame): Promise<boolean> {
                    try {
                        // Backpressure: only block when the generator's queue is actually
                        // full (desiredSize is synchronous, so steady state stays
                        // timer-free), then pace to the consumer's drain rate — bounded, so
                        // a wedged consumer costs dropped frames rather than the whole loop.
                        if ((writer.desiredSize ?? 1) <= 0) {
                            if (opts.stats) opts.stats.presentState = 'mstg:awaiting-ready';
                            try {
                                await withTimeout(writer.ready, WRITER_READY_TIMEOUT_MS, 'mstgPresent: writer.ready');
                            } catch (e: unknown) {
                                if (!(e instanceof TimeoutError))
                                    throw e;

                                if (opts.stats) opts.stats.presentState = 'mstg:ready-timeout';
                                return false;
                            }
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

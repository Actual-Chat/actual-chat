import { type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import { type DecodedFrame } from '../frame-envelopes';
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

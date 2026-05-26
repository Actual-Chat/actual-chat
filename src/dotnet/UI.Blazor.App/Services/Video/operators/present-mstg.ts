import { from, type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import { RunningEMA } from 'math';
import { delayAsync } from 'actuallab-core';
import { aggregateDropTrace, updatePlaybackRateEma, type DecodedFrame } from '../frame-envelopes';
import { FrameDropStage } from '../frame-drop-trace';

const PRESENT_SKIP_RATIO_EMA_ALPHA = 0.1;

const { warnLog } = getLogs('VideoPipeline');

const MAX_FPS = 120;
const MIN_FPS = 10;
const MIN_DURATION_MS = 1000 / MAX_FPS;
const MAX_DURATION_MS = 1000 / MIN_FPS;

// Beyond this backlog the MAX_FPS cap alone can't drain it; switch to skip-mode.
const CATCHUP_BUDGET_MS = 4_000;

export interface MstgPresentOptions {
    getWriter: () => WritableStreamDefaultWriter<VideoFrame>;
    getBufferSpanMs: () => number;
    targetSpanMs: number;
    nowFn?: () => number;
    delayFn?: (ms: number) => Promise<void>;
}

// Per frame: extra = max(0, bufferSpan - targetSpan).
// Skip (extra > CATCHUP_BUDGET_MS && now - lastWriteAt < MIN_DURATION_MS):
//   close, leave lastWriteAt untouched so the next frame still hits its slot.
// Catch-up (extra > 0): duration = MIN_DURATION_MS, present at MAX_FPS.
// Steady (extra == 0): duration = clamp(naturalDelta, MIN, MAX) — schedule
//   advances by exactly the source delta, so it tracks capture time without
//   anchor drift across the run.
export function mstgPresent(opts: MstgPresentOptions): PipeOperator<DecodedFrame, void> {
    const { getWriter, getBufferSpanMs, targetSpanMs } = opts;
    const nowFn = opts.nowFn ?? ((): number => performance.now());
    const delayFn = opts.delayFn ?? ((ms): Promise<void> => delayAsync(ms));
    return source => from(impl(source));

    async function* impl(source: AsyncIterable<DecodedFrame>): AsyncIterable<void> {
        let writer: WritableStreamDefaultWriter<VideoFrame> | null = null;
        let lastWriteAt: number | null = null;
        let prevCapturedAt: number | null = null;
        let prevCapturedEpoch: number | null = null;
        const presentSkipRatio = new RunningEMA(0, 1, PRESENT_SKIP_RATIO_EMA_ALPHA);
        try {
            for await (const decoded of source) {
                try {
                    const now = nowFn();
                    const extraMs = Math.max(0, getBufferSpanMs() - targetSpanMs);
                    if (prevCapturedEpoch !== null && decoded.capturedAt.epoch !== prevCapturedEpoch) {
                        lastWriteAt = null;
                        prevCapturedAt = null;
                    }

                    if (extraMs > CATCHUP_BUDGET_MS
                    && lastWriteAt !== null
                    && now - lastWriteAt < MIN_DURATION_MS) {
                        decoded.stats.pendingPresenterDrops++;
                        presentSkipRatio.appendSample(1);
                        decoded.stats.presentSkipRatio = presentSkipRatio.value;
                        prevCapturedAt = decoded.capturedAt.timeMs;
                        continue;
                    }
                    presentSkipRatio.appendSample(0);
                    decoded.stats.presentSkipRatio = presentSkipRatio.value;

                    let durationMs: number;
                    if (lastWriteAt === null || prevCapturedAt === null) {
                        durationMs = 0;
                    } else if (extraMs > 0) {
                        durationMs = MIN_DURATION_MS;
                    } else {
                        const natural = decoded.capturedAt.timeMs - prevCapturedAt;
                        durationMs = Math.max(MIN_DURATION_MS, Math.min(MAX_DURATION_MS, natural));
                    }

                    const baseAt: number = lastWriteAt ?? now;
                    let nextWriteAt: number = baseAt + durationMs;
                    if (nextWriteAt - now > MAX_DURATION_MS)
                        nextWriteAt = now + MAX_DURATION_MS;

                    if (nextWriteAt > now)
                        await delayFn(nextWriteAt - now);

                    writer ??= getWriter();
                    let written = false;
                    try {
                        await writer.write(decoded.frame);
                        written = true;
                    } catch (e: unknown) {
                        warnLog?.log('mstgPresent: write failed', e);
                        throw e;
                    } finally {
                        if (!written) {
                            decoded.stats.pendingPresenterDrops++;
                        }
                        else {
                        // Carry-forward Option A: every successful present
                        // attributes the upstream trace AND any presenter
                        // drops accumulated since the previous accept to
                        // the cumulative histogram, then bumps `presented`.
                            aggregateDropTrace(decoded.stats, decoded.dropTrace);
                            if (decoded.stats.pendingPresenterDrops > 0) {
                                decoded.stats.dropTrace.set(
                                    FrameDropStage.ReceiverPresent,
                                    (decoded.stats.dropTrace.get(FrameDropStage.ReceiverPresent) ?? 0)
                                    + decoded.stats.pendingPresenterDrops);
                                decoded.stats.pendingPresenterDrops = 0;
                            }
                            decoded.stats.presented++;
                            updatePlaybackRateEma(decoded.stats, decoded.capturedAt, performance.now());
                        }
                    }
                    lastWriteAt = nextWriteAt;
                    prevCapturedAt = decoded.capturedAt.timeMs;
                    prevCapturedEpoch = decoded.capturedAt.epoch;
                } finally {
                    try { decoded.frame.close(); } catch { /* already closed */ }
                }
            }
        } finally {
            try { writer?.releaseLock(); } catch { /* ignore */ }
        }
    }
}

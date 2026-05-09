import { from, type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import { delayAsync } from 'promises';
import type { DecodedFrame } from '../frame-envelopes';

const { warnLog } = getLogs('VideoPipeline');

// ---- Tunables -------------------------------------------------------------

/** Hard ceiling on display rate. Above this we skip frames after decode. */
const MAX_FPS = 120;
/** Hard floor on display rate (max gap between two consecutive presented
 *  frames). Below this we'd look choppy; cap durations there. */
const MIN_FPS = 10;
const MIN_DURATION_MS = 1000 / MAX_FPS;     // 8.33 ms — 120 fps cap
const MAX_DURATION_MS = 1000 / MIN_FPS;     // 100  ms — 10 fps floor

/**
 * Buffer overflow we promise to drain via the `MAX_FPS` cap alone — i.e.
 * if `bufferSpanMs - targetSpanMs <= CATCHUP_BUDGET_MS`, just present
 * every frame at MAX_FPS until extra hits 0. Above this, switch to
 * skip-mode: drop frames that would land within `MIN_DURATION_MS` of
 * the previous one, keep display at MAX_FPS, drain at decoder speed.
 *
 * 4 s = "1 s of wall time at 120 fps presenting source-rate (30 fps)
 * frames", i.e. 120 source-frames = 4 s of source video.
 */
const CATCHUP_BUDGET_MS = 4_000;

// ---- Options --------------------------------------------------------------

// Production binding: the writable of a `MediaStreamTrackGenerator`
// whose track is rendered via `<video>.srcObject`.
export interface MstgPresentOptions {
    getWriter: () => WritableStreamDefaultWriter<VideoFrame>;
    /** Receiver buffer's current `spanMs()`. Read fresh per frame to
     *  decide present-vs-skip; see `CATCHUP_BUDGET_MS`. */
    getBufferSpanMs: () => number;
    /** Same value passed to the buffer's `targetSpanMs`. */
    targetSpanMs: number;
    /** Test seam for `performance.now`. */
    nowFn?: () => number;
    /** Test seam for the inter-frame delay primitive. */
    delayFn?: (ms: number) => Promise<void>;
}

/**
 * Terminal sink: writes decoded `VideoFrame`s to a
 * `MediaStreamTrackGenerator.writable` at a variable cadence driven by
 * the source's capture-time deltas.
 *
 * Pacing rule per frame:
 *   - `extra = max(0, bufferSpanMs - targetSpanMs)`
 *   - `naturalDelta = decoded.capturedAt - prevCapturedAt`
 *   - **Skip** (close after decode, schedule unchanged) when
 *     `extra > CATCHUP_BUDGET_MS` AND `now - lastWriteAt < MIN_DURATION_MS`
 *     — backlog too big, this frame would push display above MAX_FPS.
 *   - **Catch-up** (`extra > 0`): `duration = MIN_DURATION_MS` —
 *     present at MAX_FPS until extra drains.
 *   - **Steady** (`extra == 0`): `duration = clamp(naturalDelta,
 *     MIN_DURATION_MS, MAX_DURATION_MS)` — present at the source's
 *     own rhythm, capped at MAX_FPS, floored at MIN_FPS.
 *
 * `nextWriteAt = lastWriteAt + duration`. Schedule advances by exactly
 * `duration` per non-skipped frame; in steady mode that sums to capture
 * time, so the schedule precisely tracks the source — no anchor drift.
 *
 * `framesPresented` counts real writes only. `framesDroppedAtPresenter`
 * covers both catch-up skips and frames whose write rejected.
 */
export function mstgPresent(opts: MstgPresentOptions): PipeOperator<DecodedFrame, void> {
    const { getWriter, getBufferSpanMs, targetSpanMs } = opts;
    const nowFn = opts.nowFn ?? ((): number => performance.now());
    const delayFn = opts.delayFn ?? ((ms): Promise<void> => delayAsync(ms));
    return source => from(impl(source));

    async function* impl(source: AsyncIterable<DecodedFrame>): AsyncIterable<void> {
        let writer: WritableStreamDefaultWriter<VideoFrame> | null = null;
        let lastWriteAt: number | null = null;
        let prevCapturedAt: number | null = null;
        for await (const decoded of source) {
            try {
                const now = nowFn();
                const extraMs = Math.max(0, getBufferSpanMs() - targetSpanMs);

                // Skip after decode: backlog too big AND we'd exceed MAX_FPS.
                // Schedule unchanged so the next non-skipped frame still lands
                // on its proper slot.
                if (extraMs > CATCHUP_BUDGET_MS
                    && lastWriteAt !== null
                    && now - lastWriteAt < MIN_DURATION_MS) {
                    decoded.stats.framesDroppedAtPresenter++;
                    prevCapturedAt = decoded.capturedAt.timeMs;
                    continue;
                }

                // Pick duration: catchup → MIN_DURATION_MS; steady →
                // clamp(naturalDelta, MIN, MAX). First frame: 0.
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
                // Defensive cap: sleep at most MAX_DURATION_MS in one go
                // (shouldn't fire — the math above already produces durations
                // ≤ MAX_DURATION_MS — but it bounds pathological cases).
                if (nextWriteAt - now > MAX_DURATION_MS)
                    nextWriteAt = now + MAX_DURATION_MS;

                if (nextWriteAt > now)
                    await delayFn(nextWriteAt - now);

                writer ??= getWriter();
                let written = false;
                try {
                    await writer.write(decoded.frame);
                    decoded.stats.framesPresented++;
                    written = true;
                } catch (e: unknown) {
                    warnLog?.log('mstgPresent: write failed', e);
                    throw e;
                } finally {
                    if (!written)
                        decoded.stats.framesDroppedAtPresenter++;
                }
                lastWriteAt = nextWriteAt;
                prevCapturedAt = decoded.capturedAt.timeMs;
            } finally {
                try { decoded.frame.close(); } catch { /* already closed */ }
            }
        }
    }
}

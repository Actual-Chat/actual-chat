import { from, type PipeOperator } from 'ix-ext';
import { delayAsync } from 'promises';
import type { DecodedFrame } from '../frame-envelopes';

// ---- Tunables -------------------------------------------------------------

/** 60 fps slot. Min wallclock gap between two consecutive presented frames. */
const PRESENT_PERIOD_MS = 1000 / 60;

/**
 * Buffer overflow we promise to drain via the 60 fps cap alone — i.e.
 * if `bufferSpanMs - targetSpanMs <= CATCHUP_BUDGET_MS`, just present
 * every frame (the cap is enough to pull the buffer back to target
 * within ~1 s wall time, assuming 30 fps capture). Above this, switch
 * to skip-mode: drop frames that would land within the 60 fps slot,
 * keep the cadence at exactly 60 fps, drain the backlog at the
 * decoder's full speed.
 */
const CATCHUP_BUDGET_MS = 1000;

/**
 * Hard reset for `nextPresentMs` when wallclock has outrun the schedule
 * by this much — the schedule is too far gone to be useful (tab in
 * background, GC stall, render thread blocked). Take the visible
 * discontinuity once, re-pin to `performance.now()`, resume the fixed
 * cadence from the new anchor.
 */
const MAX_PRESENT_DURATION_MS = 200;

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
 * `MediaStreamTrackGenerator.writable` at a fixed 60 fps cadence.
 *
 * Pacing rule:
 *   - `extra = max(0, bufferSpanMs - targetSpanMs)`
 *   - In budget (`extra <= CATCHUP_BUDGET_MS`): present every frame,
 *     pace via `delayAsync` so wallclock writes are spaced ≥
 *     `PRESENT_PERIOD_MS` (60 fps cap). Backlog drains at cap rate
 *     (1 s of capture per 1 s wall above source rate).
 *   - Out of budget: skip frames that would land within the cap slot
 *     (`now - lastPresentMs < PRESENT_PERIOD_MS`). The frame is decoded
 *     for codec correctness, then closed instead of written. Display
 *     stays at 60 fps; backlog drains at decoder's full speed.
 *
 * Cadence is anchored — `nextPresentMs += PRESENT_PERIOD_MS` regardless
 * of actual write time — so a 5 ms hiccup doesn't shift the schedule.
 * `MAX_PRESENT_DURATION_MS` re-pins after a long stall.
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
        let lastPresentMs = Number.NEGATIVE_INFINITY;
        let nextPresentMs: number | null = null;
        for await (const decoded of source) {
            try {
                const now = nowFn();
                const extraMs = Math.max(0, getBufferSpanMs() - targetSpanMs);
                const inBudget = extraMs <= CATCHUP_BUDGET_MS;

                // Skip after decode: out-of-budget AND we'd land too soon
                // after the previous present. The frame is closed in
                // the outer finally.
                if (!inBudget && now - lastPresentMs < PRESENT_PERIOD_MS) {
                    decoded.stats.framesDroppedAtPresenter++;
                    continue;
                }

                // Pace so writes are spaced at PRESENT_PERIOD_MS. Out-of-
                // budget frames usually need no sleep (we got here only
                // because lastPresentMs was ≥ a period ago) but the
                // schedule still advances uniformly below.
                if (nextPresentMs === null) {
                    nextPresentMs = now;
                }
                else if (nextPresentMs > now) {
                    await delayFn(nextPresentMs - now);
                }
                else if (now - nextPresentMs > MAX_PRESENT_DURATION_MS) {
                    nextPresentMs = nowFn();
                }

                writer ??= getWriter();
                let written = false;
                try {
                    await writer.write(decoded.frame);
                    decoded.stats.framesPresented++;
                    written = true;
                } finally {
                    if (!written)
                        decoded.stats.framesDroppedAtPresenter++;
                }
                lastPresentMs = nowFn();
                nextPresentMs += PRESENT_PERIOD_MS;
            } finally {
                try { decoded.frame.close(); } catch { /* already closed */ }
            }
        }
    }
}

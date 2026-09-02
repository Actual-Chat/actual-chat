import { MonotonicClock } from 'clocks';
import { getLogs } from 'logging';
import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

const { warnLog } = getLogs('VideoPipeline');

export interface StampCaptureTimeOptions {
    clock?: MonotonicClock;
}

// How far the source timeline may drift from ours before it stops counting as a
// capture clock. Anchored on the first frame, so a constant pipeline delay is free.
const MAX_SOURCE_OFFSET_MS = 100;

// Consecutive violations before the source clock is abandoned for the run. Two
// tells a stopped or repeating clock from one late frame.
const MAX_STRIKES = 2;

// Forward step enforcing the strictly-increasing invariant.
const MIN_STEP_MS = 1;

/** On clock-epoch flip (sleep / NTP step) sets forceKeyframe so the receiver
 *  can rebase its decode anchors. */
export function stampCaptureTime(opts: StampCaptureTimeOptions = {}): PipeOperator<CapturedFrame, CapturedFrame> {
    const clock = opts.clock ?? new MonotonicClock({ minTickMs: 33 });
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            // -1 sentinel: first frame always trips epochChanged.
            let lastEpoch = -1;
            // Anchor mapping the frame's own capture clock onto ours, taken from the
            // first frame so only real drift accumulates against it.
            let originClockMs: number | null = null;
            let originTimestampUs = 0;
            let lastTimeMs = Number.NEGATIVE_INFINITY;
            let strikes = 0;
            let useWallClock = false;
            for await (const envelope of source) {
                let mustClose = true;
                try {
                    const wallNow = clock.now();
                    let capturedAt = wallNow;
                    const timestampUs = envelope.frame.timestamp;
                    // Trust the source clock only while it keeps tracking ours. One that
                    // stops (Firefox reports mediaTime as a constant 0 for a MediaStream),
                    // repeats, or drifts is abandoned for the run — at the cost of a
                    // mis-stamped frame or two, against a whole run of invented times.
                    if (!useWallClock && originClockMs !== null && Number.isFinite(timestampUs)) {
                        const sourceMs = originClockMs + (timestampUs - originTimestampUs) / 1000;
                        const offsetMs = sourceMs - wallNow.timeMs;
                        if (Math.abs(offsetMs) <= MAX_SOURCE_OFFSET_MS && sourceMs > lastTimeMs) {
                            capturedAt = { timeMs: sourceMs, epoch: wallNow.epoch };
                            strikes = 0;
                        } else if (++strikes >= MAX_STRIKES) {
                            useWallClock = true;
                            warnLog?.log(
                                `stampCaptureTime: source clock abandoned after ${strikes} strikes `
                                + `(offset=${offsetMs.toFixed(0)}ms, advanced=`
                                + `${(sourceMs - lastTimeMs).toFixed(1)}ms) - using the monotonic clock`);
                        }
                    }
                    if (originClockMs === null) {
                        originClockMs = wallNow.timeMs;
                        originTimestampUs = Number.isFinite(timestampUs) ? timestampUs : 0;
                    }
                    const epochChanged = capturedAt.epoch !== lastEpoch;
                    if (epochChanged) {
                        lastEpoch = capturedAt.epoch;
                        // A sleep/NTP step moves our clock, not the camera's - re-anchor
                        // so the two don't stay offset by the size of the jump, and give
                        // the source clock a fresh hearing.
                        originClockMs = wallNow.timeMs;
                        originTimestampUs = Number.isFinite(timestampUs) ? timestampUs : 0;
                        capturedAt = wallNow;
                        strikes = 0;
                        useWallClock = false;
                    }
                    // The invariant everything downstream relies on: capture time is strictly
                    // increasing within an epoch (offsets are derived from it, and a backwards
                    // step collapses them to 0). An epoch flip is the deliberate exception:
                    // the receiver rebases there.
                    if (!epochChanged && capturedAt.timeMs <= lastTimeMs)
                        capturedAt = { timeMs: lastTimeMs + MIN_STEP_MS, epoch: capturedAt.epoch };
                    lastTimeMs = capturedAt.timeMs;
                    // Index is assigned at the source (mstpSource); preserve it
                    // so flood-gate drops show as gaps downstream.
                    const output = {
                        ...envelope,
                        capturedAt,
                        forceKeyframe: envelope.forceKeyframe || epochChanged,
                    };
                    mustClose = false;
                    yield output;
                } finally {
                    if (mustClose)
                        try { envelope.frame.close(); } catch { /* ignore */ }
                }
            }
        }
    };
}

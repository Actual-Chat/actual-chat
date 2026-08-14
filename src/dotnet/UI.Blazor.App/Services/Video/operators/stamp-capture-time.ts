import { MonotonicClock } from 'clocks';
import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

export interface StampCaptureTimeOptions {
    clock?: MonotonicClock;
}

// Beyond this the hardware timeline is not tracking ours - a paused/resumed
// track, a clock the platform rebased, or a source whose timestamps aren't a
// real capture clock at all. Fall back to the wall clock and re-anchor.
const MAX_CAPTURE_DRIFT_MS = 1000;

// Consecutive unusable frames tolerated before the anchor is abandoned. A blip
// (one late or duplicated timestamp) must not cost a working source clock.
const MAX_BAD_FRAME_RUN = 5;

// Forward step used to bridge a blip while staying on the source timeline.
const MIN_STEP_MS = 1;

// On clock-epoch flip (sleep / NTP step) sets forceKeyframe so the receiver
// can rebase its decode anchors.
export function stampCaptureTime(opts: StampCaptureTimeOptions = {}): PipeOperator<CapturedFrame, CapturedFrame> {
    const clock = opts.clock ?? new MonotonicClock({ minTickMs: 33 });
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            // -1 sentinel: first frame always trips epochChanged.
            let lastEpoch = -1;
            // Anchor mapping the frame's own capture clock onto ours. `clock.now()`
            // is sampled HERE, several operators and one single-threaded worker
            // downstream of the camera, so its deltas measure our scheduling rather
            // than the source's cadence - and those deltas are what paces playback
            // on the receiver. VideoFrame.timestamp is the capture clock, so take
            // the origin from ours (the receiver compares against wall time) and
            // every step after it from the frame.
            let originClockMs: number | null = null;
            let originTimestampUs = 0;
            let lastTimeMs = Number.NEGATIVE_INFINITY;
            let badRun = 0;
            for await (const envelope of source) {
                let mustClose = true;
                try {
                    const wallNow = clock.now();
                    let capturedAt = wallNow;
                    const timestampUs = envelope.frame.timestamp;
                    if (originClockMs !== null && Number.isFinite(timestampUs)) {
                        const timeMs = originClockMs + (timestampUs - originTimestampUs) / 1000;
                        // Two ways the source clock can be unusable: it drifts away from ours
                        // (paused track, platform rebase, or these aren't capture timestamps at
                        // all), or it stops advancing - a source that leaves timestamp at 0 would
                        // otherwise collapse every frame onto one instant and take the whole
                        // downstream timeline with it.
                        //
                        // A single bad frame must NOT cost us the anchor. Our timestamps are
                        // capture time and the wall clock is stamp time, so the source timeline
                        // runs permanently BEHIND wall by the pipeline delay: emit one wall value
                        // and every later source value fails the monotonic test against it, which
                        // strands us on the wall clock for the rest of the run. So a blip just
                        // steps forward on the source timeline and keeps the anchor, and only a
                        // sustained run of failures gives up and re-anchors.
                        const isUsable = Math.abs(timeMs - wallNow.timeMs) <= MAX_CAPTURE_DRIFT_MS
                            && timeMs > lastTimeMs;
                        if (isUsable) {
                            capturedAt = { timeMs, epoch: wallNow.epoch };
                            badRun = 0;
                        } else if (++badRun < MAX_BAD_FRAME_RUN) {
                            capturedAt = { timeMs: lastTimeMs + MIN_STEP_MS, epoch: wallNow.epoch };
                        } else {
                            originClockMs = null;
                            badRun = 0;
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
                        // so the two don't stay offset by the size of the jump.
                        originClockMs = wallNow.timeMs;
                        originTimestampUs = Number.isFinite(timestampUs) ? timestampUs : 0;
                        capturedAt = wallNow;
                    }
                    // The invariant everything downstream relies on: capture time is strictly
                    // increasing within an epoch (offsets are derived from it, and a backwards
                    // step collapses them to 0). Every branch above can violate it - the source
                    // clock can stall or repeat, and the wall clock we fall back to may sit
                    // behind the source timeline we were stepping along. Enforce it here once
                    // rather than trusting each branch to. An epoch flip is the deliberate
                    // exception: the receiver rebases there.
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

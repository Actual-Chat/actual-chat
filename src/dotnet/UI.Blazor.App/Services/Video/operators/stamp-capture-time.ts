import { MonotonicClock } from 'clocks';
import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

export interface StampCaptureTimeOptions {
    clock?: MonotonicClock;
}

// On clock-epoch flip (sleep / NTP step) sets forceKeyframe so the receiver
// can rebase its decode anchors.
export function stampCaptureTime(opts: StampCaptureTimeOptions = {}): PipeOperator<CapturedFrame, CapturedFrame> {
    const clock = opts.clock ?? new MonotonicClock({ minTickMs: 33 });
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            // -1 sentinel: first frame always trips epochChanged.
            let lastEpoch = -1;
            for await (const envelope of source) {
                let mustClose = true;
                try {
                    const capturedAt = clock.now();
                    const epochChanged = capturedAt.epoch !== lastEpoch;
                    if (epochChanged) {
                        lastEpoch = capturedAt.epoch;
                    }
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

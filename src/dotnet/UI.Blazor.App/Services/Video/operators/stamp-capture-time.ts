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
            let nextIndex = 0;
            for await (const envelope of source) {
                let mustClose = true;
                try {
                    const capturedAt = clock.now();
                    const epochChanged = capturedAt.epoch !== lastEpoch;
                    if (epochChanged) {
                        envelope.stats.lastCapturedEpoch = capturedAt.epoch;
                        lastEpoch = capturedAt.epoch;
                    }
                    const output = {
                        ...envelope,
                        capturedAt,
                        index: nextIndex++,
                        forceKeyframe: epochChanged,
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

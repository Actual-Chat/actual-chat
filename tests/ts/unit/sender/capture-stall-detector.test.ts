import { describe, expect, it } from 'vitest';
import {
    CaptureStallDetector,
    type CaptureStallSample,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/capture-stall-detector';

function sample(over: Partial<CaptureStallSample> = {}): CaptureStallSample {
    return {
        isRecording: true,
        isScreencast: false,
        isTabBackgrounded: false,
        isTrackLive: true,
        isRecoveryScheduled: false,
        framesCaptured: 10,
        ...over,
    };
}

describe('CaptureStallDetector', () => {
    it('reports a stall once foreground camera capture flatlines for the threshold', () => {
        const d = new CaptureStallDetector(3_000);
        expect(d.onSample(sample({ framesCaptured: 10 }), 0)).toBe(false);
        expect(d.onSample(sample({ framesCaptured: 10 }), 1_000)).toBe(false);
        expect(d.onSample(sample({ framesCaptured: 10 }), 2_000)).toBe(false);
        expect(d.onSample(sample({ framesCaptured: 10 }), 4_000)).toBe(true);
    });

    it('re-arms after a stall so the next flatline is reported again', () => {
        const d = new CaptureStallDetector(3_000);
        d.onSample(sample({ framesCaptured: 10 }), 0);
        d.onSample(sample({ framesCaptured: 10 }), 1_000);
        expect(d.onSample(sample({ framesCaptured: 10 }), 4_000)).toBe(true);
        expect(d.onSample(sample({ framesCaptured: 10 }), 5_000)).toBe(false);
        expect(d.onSample(sample({ framesCaptured: 10 }), 9_000)).toBe(true);
    });

    it('resets when frames arrive', () => {
        const d = new CaptureStallDetector(3_000);
        d.onSample(sample({ framesCaptured: 10 }), 0);
        d.onSample(sample({ framesCaptured: 10 }), 1_000);
        d.onSample(sample({ framesCaptured: 11 }), 2_000);
        expect(d.onSample(sample({ framesCaptured: 11 }), 4_500)).toBe(false);
        expect(d.onSample(sample({ framesCaptured: 11 }), 5_500)).toBe(false);
        expect(d.onSample(sample({ framesCaptured: 11 }), 7_500)).toBe(true);
    });

    it('never reports a stall for a screencast, whose idle screen legitimately captures nothing', () => {
        const d = new CaptureStallDetector(3_000);
        for (let t = 0; t <= 30_000; t += 1_000)
            expect(d.onSample(sample({ isScreencast: true, framesCaptured: 1 }), t)).toBe(false);
    });

    it('ignores backgrounded tabs, dead tracks, pending recovery and non-recording states', () => {
        const d = new CaptureStallDetector(3_000);
        d.onSample(sample(), 0);
        d.onSample(sample(), 1_000);
        expect(d.onSample(sample({ isTabBackgrounded: true }), 4_000)).toBe(false);
        expect(d.onSample(sample({ isTrackLive: false }), 8_000)).toBe(false);
        expect(d.onSample(sample({ isRecoveryScheduled: true }), 12_000)).toBe(false);
        expect(d.onSample(sample({ isRecording: false }), 16_000)).toBe(false);
        // Each of those resets the window, so a fresh flatline needs the full threshold again.
        expect(d.onSample(sample(), 17_000)).toBe(false);
        expect(d.onSample(sample(), 19_000)).toBe(false);
        expect(d.onSample(sample(), 20_500)).toBe(true);
    });
});

import { describe, it, expect } from 'vitest';
import { forceKeyframeOnDimChange } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/force-keyframe-on-dim-change';
import {
    createEmptyRecorderStats,
    type CapturedFrame,
    type RecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
// ---- Mocks ----------------------------------------------------------------

class MockVideoFrame {
    constructor(
        public id: number,
        public codedWidth: number,
        public codedHeight: number,
    ) {}
    close(): void { /* noop */ }
}

const mkFrame = (id: number, w: number, h: number): MockVideoFrame => new MockVideoFrame(id, w, h);

// ---- Helpers --------------------------------------------------------------

function envelopeFor(
    stats: RecorderStats,
    frame: MockVideoFrame,
    index = 0,
    forceKeyframe = false,
): CapturedFrame {
    return {
        frame: frame as unknown as VideoFrame,
        capturedAt: { timeMs: 100 + index, epoch: 0 },
        durationUs: 1_000_000 / 30,
        index,
        dropTrace: [],
        sourceWidth: frame.codedWidth,
        sourceHeight: frame.codedHeight,
        forceKeyframe,
        rotation: 0,
        stats,
    };
}

function source(items: CapturedFrame[]): AsyncIterable<CapturedFrame> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

// ---- Tests ----------------------------------------------------------------

describe('forceKeyframeOnDimChange', () => {
    it('same dims across frames → forceKeyframe unchanged', async () => {
        const stats = createEmptyRecorderStats();
        const op = forceKeyframeOnDimChange();
        const envelopes = [
            envelopeFor(stats, mkFrame(1, 1920, 1080), 0, false),
            envelopeFor(stats, mkFrame(2, 1920, 1080), 1, false),
            envelopeFor(stats, mkFrame(3, 1920, 1080), 2, false),
        ];
        const out = await drain(op(source(envelopes)));

        expect(out.map(e => e.forceKeyframe)).toEqual([false, false, false]);
    });

    it('dim change → forceKeyframe = true on the first frame at new dims', async () => {
        const stats = createEmptyRecorderStats();
        const op = forceKeyframeOnDimChange();
        const envelopes = [
            envelopeFor(stats, mkFrame(1, 1920, 1080), 0, false),
            envelopeFor(stats, mkFrame(2, 1920, 1080), 1, false),
            envelopeFor(stats, mkFrame(3, 1280, 720), 2, false),  // dim change → key
            envelopeFor(stats, mkFrame(4, 1280, 720), 3, false),  // stable → not keyed
        ];
        const out = await drain(op(source(envelopes)));

        expect(out.map(e => e.forceKeyframe)).toEqual([false, false, true, false]);
    });

    it('first frame: prior forceKeyframe value is preserved (we do not override it)', async () => {
        const stats = createEmptyRecorderStats();
        const op = forceKeyframeOnDimChange();
        // Upstream stampCaptureTime sets forceKeyframe=true on the first frame.
        const envelopes = [
            envelopeFor(stats, mkFrame(1, 1920, 1080), 0, true),
            envelopeFor(stats, mkFrame(2, 1920, 1080), 1, false),
        ];
        const out = await drain(op(source(envelopes)));

        // First-frame upstream-true is preserved.
        expect(out[0].forceKeyframe).toBe(true);
        // Same-dim follow-up: no change.
        expect(out[1].forceKeyframe).toBe(false);
    });

    it('upstream-set true is never lowered by this operator (raises only)', async () => {
        const stats = createEmptyRecorderStats();
        const op = forceKeyframeOnDimChange();
        const envelopes = [
            envelopeFor(stats, mkFrame(1, 1920, 1080), 0, false),
            envelopeFor(stats, mkFrame(2, 1920, 1080), 1, true),  // upstream-true at stable dims
        ];
        const out = await drain(op(source(envelopes)));

        expect(out[1].forceKeyframe).toBe(true);
    });

    it('multiple successive dim changes each force a keyframe', async () => {
        const stats = createEmptyRecorderStats();
        const op = forceKeyframeOnDimChange();
        const envelopes = [
            envelopeFor(stats, mkFrame(1, 1920, 1080), 0, false),
            envelopeFor(stats, mkFrame(2, 1280, 720), 1, false),  // change
            envelopeFor(stats, mkFrame(3, 640, 360), 2, false),   // another change
            envelopeFor(stats, mkFrame(4, 640, 360), 3, false),   // stable
        ];
        const out = await drain(op(source(envelopes)));

        expect(out.map(e => e.forceKeyframe)).toEqual([false, true, true, false]);
    });

    it('preserves all other envelope fields (no mutation of input)', async () => {
        const stats = createEmptyRecorderStats();
        const op = forceKeyframeOnDimChange();
        const f1 = mkFrame(1, 1920, 1080);
        const f2 = mkFrame(2, 1280, 720);
        const envelopes = [
            envelopeFor(stats, f1, 0, false),
            envelopeFor(stats, f2, 1, false),
        ];
        // Capture original references.
        const originalE1 = envelopes[0];

        const out = await drain(op(source(envelopes)));
        expect(out[0].frame).toBe(f1 as unknown as VideoFrame);
        expect(out[1].frame).toBe(f2 as unknown as VideoFrame);
        expect(out[0].index).toBe(0);
        expect(out[1].index).toBe(1);
        // Input envelope is not mutated (shallow-clone on flag raise).
        expect(originalE1.forceKeyframe).toBe(false);
    });
});

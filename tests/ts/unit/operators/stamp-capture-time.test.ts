import { describe, it, expect } from 'vitest';
import type { MonotonicClock, MonotonicTime } from 'clocks';
import { stampCaptureTime } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/stamp-capture-time';
import {
    createEmptyRecorderStats,
    type CapturedFrame,
    type RecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

// ---- Helpers --------------------------------------------------------------

class FakeClock {
    private idx = 0;
    constructor(private readonly samples: MonotonicTime[]) {}
    now(): MonotonicTime {
        if (this.idx >= this.samples.length) {
            // Repeat the last sample for any extra calls.
            return this.samples[this.samples.length - 1];
        }
        return this.samples[this.idx++];
    }
}

function envelopesFor(stats: RecorderStats, count: number): CapturedFrame[] {
    return Array.from({ length: count }, (_, i): CapturedFrame => ({
        // An opaque object stands in for VideoFrame; the operator never reads it.
        frame: { id: i } as unknown as VideoFrame,
        capturedAt: { timeMs: 0, epoch: 0 },
        durationUs: 1_000_000 / 30,
        index: i,
        dropTrace: [],
        sourceWidth: 0,
        sourceHeight: 0,
        forceKeyframe: false,
        rotation: 0,
        stats,
    }));
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

describe('stampCaptureTime', () => {
    it('first frame: forceKeyframe=true (epoch is new vs the −1 sentinel)', async () => {
        const stats = createEmptyRecorderStats();
        const clock = new FakeClock([{ timeMs: 100, epoch: 0 }]) as unknown as MonotonicClock;
        const op = stampCaptureTime({ clock });
        const out = await drain(op(source(envelopesFor(stats, 1))));
        expect(out).toHaveLength(1);
        expect(out[0].forceKeyframe).toBe(true);
        expect(out[0].capturedAt).toEqual({ timeMs: 100, epoch: 0 });
        expect(out[0].index).toBe(0);
    });

    it('timeMs and index advance per item', async () => {
        const stats = createEmptyRecorderStats();
        const clock = new FakeClock([
            { timeMs: 100, epoch: 0 },
            { timeMs: 133, epoch: 0 },
            { timeMs: 166, epoch: 0 },
            { timeMs: 199, epoch: 0 },
        ]) as unknown as MonotonicClock;
        const op = stampCaptureTime({ clock });
        const out = await drain(op(source(envelopesFor(stats, 4))));
        expect(out.map(e => e.capturedAt.timeMs)).toEqual([100, 133, 166, 199]);
        expect(out.map(e => e.index)).toEqual([0, 1, 2, 3]);
    });

    it('only the first frame at a stable epoch has forceKeyframe=true', async () => {
        const stats = createEmptyRecorderStats();
        const clock = new FakeClock([
            { timeMs: 10, epoch: 0 },
            { timeMs: 20, epoch: 0 },
            { timeMs: 30, epoch: 0 },
        ]) as unknown as MonotonicClock;
        const op = stampCaptureTime({ clock });
        const out = await drain(op(source(envelopesFor(stats, 3))));
        expect(out.map(e => e.forceKeyframe)).toEqual([true, false, false]);
    });

    it('mid-stream epoch bump flips forceKeyframe=true exactly once', async () => {
        const stats = createEmptyRecorderStats();
        // Frames 0,1,2 at epoch 0; frame 3 bumps to epoch 1; frames 4,5 stay at epoch 1.
        const clock = new FakeClock([
            { timeMs: 100, epoch: 0 },
            { timeMs: 133, epoch: 0 },
            { timeMs: 166, epoch: 0 },
            { timeMs: 5_000, epoch: 1 },
            { timeMs: 5_033, epoch: 1 },
            { timeMs: 5_066, epoch: 1 },
        ]) as unknown as MonotonicClock;
        const op = stampCaptureTime({ clock });
        const out = await drain(op(source(envelopesFor(stats, 6))));
        expect(out.map(e => e.forceKeyframe)).toEqual([true, false, false, true, false, false]);
    });

    it('preserves passthrough envelope fields (frame, sourceWidth/Height, stats ref)', async () => {
        const stats = createEmptyRecorderStats();
        const op = stampCaptureTime({
            clock: new FakeClock([{ timeMs: 100, epoch: 0 }]) as unknown as MonotonicClock,
        });
        const input = envelopesFor(stats, 1);
        input[0].sourceWidth = 1920;
        input[0].sourceHeight = 1080;
        const inputFrame = input[0].frame;

        const out = await drain(op(source(input)));
        expect(out[0].frame).toBe(inputFrame);
        expect(out[0].sourceWidth).toBe(1920);
        expect(out[0].sourceHeight).toBe(1080);
        expect(out[0].stats).toBe(stats);
    });

    it('uses a default clock when none is supplied (no throw)', async () => {
        const stats = createEmptyRecorderStats();
        const op = stampCaptureTime();
        const out = await drain(op(source(envelopesFor(stats, 2))));
        expect(out).toHaveLength(2);
        // First frame is keyed; second is not (epochs match because the
        // real clock doesn't bump under stable conditions).
        expect(out[0].forceKeyframe).toBe(true);
        expect(out[1].forceKeyframe).toBe(false);
        // Indices increment as expected.
        expect(out.map(e => e.index)).toEqual([0, 1]);
    });

    it('multiple consecutive epoch bumps each force a keyframe', async () => {
        const stats = createEmptyRecorderStats();
        const clock = new FakeClock([
            { timeMs: 100, epoch: 0 },
            { timeMs: 200, epoch: 1 }, // bump
            { timeMs: 300, epoch: 2 }, // bump again
            { timeMs: 400, epoch: 2 }, // stable
        ]) as unknown as MonotonicClock;
        const op = stampCaptureTime({ clock });
        const out = await drain(op(source(envelopesFor(stats, 4))));
        expect(out.map(e => e.forceKeyframe)).toEqual([true, true, true, false]);
        expect(out.map(e => e.index)).toEqual([0, 1, 2, 3]);
    });
});

// ---- Source-clock validation ----------------------------------------------

function envelopesWithTimestamps(stats: RecorderStats, timestampsUs: number[]): CapturedFrame[] {
    return timestampsUs.map((timestamp, i): CapturedFrame => ({
        frame: { id: i, timestamp } as unknown as VideoFrame,
        capturedAt: { timeMs: 0, epoch: 0 },
        durationUs: 1_000_000 / 30,
        index: i,
        dropTrace: [],
        sourceWidth: 0,
        sourceHeight: 0,
        forceKeyframe: false,
        rotation: 0,
        stats,
    }));
}

function wallSamples(startMs: number, stepMs: number, count: number): MonotonicTime[] {
    return Array.from({ length: count }, (_, i) => ({ timeMs: startMs + i * stepMs, epoch: 0 }));
}

describe('stampCaptureTime source-clock validation', () => {
    it('rides the source clock while it tracks ours', async () => {
        const stats = createEmptyRecorderStats();
        const clock = new FakeClock(wallSamples(1000, 33, 5)) as unknown as MonotonicClock;
        // Source advances 33ms per frame, same as wall, offset by a pipeline delay.
        const op = stampCaptureTime({ clock });
        const out = await drain(op(source(envelopesWithTimestamps(stats,
            [500_000, 533_000, 566_000, 599_000, 632_000]))));
        const deltas = out.slice(1).map((e, i) => e.capturedAt.timeMs - out[i].capturedAt.timeMs);
        deltas.forEach(d => expect(d).toBeCloseTo(33, 1));
    });

    it('abandons a stopped clock within two frames instead of inventing 1ms steps', async () => {
        const stats = createEmptyRecorderStats();
        const clock = new FakeClock(wallSamples(1000, 33, 6)) as unknown as MonotonicClock;
        // Firefox reports mediaTime as a constant 0 for a MediaStream.
        const op = stampCaptureTime({ clock });
        const out = await drain(op(source(envelopesWithTimestamps(stats, [0, 0, 0, 0, 0, 0]))));
        const times = out.map(e => e.capturedAt.timeMs);
        const tail = times.slice(3);
        const deltas = tail.slice(1).map((t, i) => t - tail[i]);
        // Wall-paced, not the 1ms fallback that collapses the receiver's timeline.
        deltas.forEach(d => expect(d).toBeGreaterThan(10));
    });

    it('abandons a clock that drifts past the offset bound', async () => {
        const stats = createEmptyRecorderStats();
        const clock = new FakeClock(wallSamples(1000, 33, 6)) as unknown as MonotonicClock;
        // Source runs at ~5x wall: offset passes 100ms within a few frames.
        const op = stampCaptureTime({ clock });
        const out = await drain(op(source(envelopesWithTimestamps(stats,
            [0, 165_000, 330_000, 495_000, 660_000, 825_000]))));
        const times = out.map(e => e.capturedAt.timeMs);
        const tail = times.slice(3);
        const deltas = tail.slice(1).map((t, i) => t - tail[i]);
        // Once abandoned it paces off our clock, so ~33ms rather than the source's 165ms.
        deltas.forEach(d => expect(d).toBeLessThan(100));
    });

    it('keeps the source clock across a single bad frame', async () => {
        const stats = createEmptyRecorderStats();
        const clock = new FakeClock(wallSamples(1000, 33, 6)) as unknown as MonotonicClock;
        // One repeated timestamp, then the source recovers.
        const op = stampCaptureTime({ clock });
        const out = await drain(op(source(envelopesWithTimestamps(stats,
            [500_000, 533_000, 533_000, 599_000, 632_000, 665_000]))));
        const times = out.map(e => e.capturedAt.timeMs);
        expect(times[times.length - 1] - times[times.length - 2]).toBeCloseTo(33, 1);
    });
});

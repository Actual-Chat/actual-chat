import { describe, it, expect, vi, afterEach } from 'vitest';
import { PaceState, temporalPace } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/temporal-pace';
import {
    createEmptyRecorderStats,
    type CapturedFrame,
    type RecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

class MockVideoFrame {
    closed = false;
    constructor(public id: number) {}
    close(): void { this.closed = true; }
}

let nowMs = 0;

function envelope(stats: RecorderStats, frame: MockVideoFrame, index: number): CapturedFrame {
    return {
        frame: frame as unknown as VideoFrame,
        capturedAt: { timeMs: 100 + index, epoch: 0 },
        index,
        dropTrace: [],
        sourceWidth: 0,
        sourceHeight: 0,
        forceKeyframe: false,
        rotation: 0,
        stats,
    };
}

// Yields frames stamping the mocked clock at `timesMs[i]` right before each.
function timedSource(stats: RecorderStats, frames: MockVideoFrame[], timesMs: number[]): AsyncIterable<CapturedFrame> {
    return (async function* () {
        await Promise.resolve();
        for (let i = 0; i < frames.length; i++) {
            nowMs = timesMs[i];
            yield envelope(stats, frames[i], i);
        }
    })();
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

afterEach(() => { vi.restoreAllMocks(); });

describe('PaceState', () => {
    it('defaults to no pacing (infinite fps)', () => {
        expect(new PaceState().targetFps).toBe(Number.POSITIVE_INFINITY);
    });
    it('setTargetFps updates the target', () => {
        const s = new PaceState();
        s.setTargetFps(10);
        expect(s.targetFps).toBe(10);
    });
});

describe('temporalPace operator', () => {
    it('passes every frame at the default (no pacing) and counts framesOffered', async () => {
        const stats = createEmptyRecorderStats();
        const frames = [new MockVideoFrame(1), new MockVideoFrame(2), new MockVideoFrame(3)];
        vi.spyOn(performance, 'now').mockImplementation(() => nowMs);

        const out = await drain(temporalPace(new PaceState())(timedSource(stats, frames, [0, 33, 66])));

        expect(out.map(e => (e.frame as unknown as MockVideoFrame).id)).toEqual([1, 2, 3]);
        expect(frames.every(f => !f.closed)).toBe(true);
        expect(stats.framesOffered).toBe(3);
    });

    it('targetFps <= 0 drops every frame (idle); framesOffered stays 0', async () => {
        const stats = createEmptyRecorderStats();
        const state = new PaceState();
        state.setTargetFps(0);
        const frames = [new MockVideoFrame(1), new MockVideoFrame(2)];
        vi.spyOn(performance, 'now').mockImplementation(() => nowMs);

        const out = await drain(temporalPace(state)(timedSource(stats, frames, [0, 33])));

        expect(out).toHaveLength(0);
        expect(frames.every(f => f.closed)).toBe(true);
        expect(stats.framesOffered).toBe(0);
    });

    it('paces 30fps input down to ~10fps (keeps ~1 in 3)', async () => {
        const stats = createEmptyRecorderStats();
        const state = new PaceState();
        state.setTargetFps(10); // min interval 100ms
        // 30fps capture arrival times.
        const times = [0, 33, 66, 100, 133, 166, 200, 233, 266, 300];
        const frames = times.map((_, i) => new MockVideoFrame(i));
        vi.spyOn(performance, 'now').mockImplementation(() => nowMs);

        const out = await drain(temporalPace(state)(timedSource(stats, frames, times)));

        // Emits at 0, 100, 200, 300 → 4 of 10.
        expect(out.map(e => (e.frame as unknown as MockVideoFrame).id)).toEqual([0, 3, 6, 9]);
        expect(stats.framesOffered).toBe(4);
    });

    it('tolerates VideoFrame.close() throwing on a dropped frame', async () => {
        const stats = createEmptyRecorderStats();
        const state = new PaceState();
        state.setTargetFps(0);
        const frame = { close: () => { throw new Error('synthetic'); } } as unknown as VideoFrame;
        const env: CapturedFrame = {
            frame,
            capturedAt: { timeMs: 0, epoch: 0 },
            index: 0,
            dropTrace: [],
            sourceWidth: 0,
            sourceHeight: 0,
            forceKeyframe: false,
            rotation: 0,
            stats,
        };
        vi.spyOn(performance, 'now').mockImplementation(() => nowMs);

        const out = await drain(temporalPace(state)((async function* () { await Promise.resolve(); yield env; })()));

        expect(out).toHaveLength(0);
        expect(stats.framesOffered).toBe(0);
    });
});

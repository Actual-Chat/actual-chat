import { describe, it, expect } from 'vitest';
import { FloodGate, floodGate } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/flood-gate';
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

describe('FloodGate', () => {
    it('starts open with skipCount = 0', () => {
        const gate = new FloodGate();
        expect(gate.isOpen).toBe(true);
        expect(gate.skipCount).toBe(0);
    });

    it('open / close toggle', () => {
        const gate = new FloodGate();
        gate.close();
        expect(gate.isOpen).toBe(false);
        gate.open();
        expect(gate.isOpen).toBe(true);
    });
});

describe('floodGate operator', () => {
    it('passes frames through while gate is open', async () => {
        const stats = createEmptyRecorderStats();
        const gate = new FloodGate();
        const frames = [new MockVideoFrame(1), new MockVideoFrame(2), new MockVideoFrame(3)];
        const envelopes = frames.map((f, i) => envelope(stats, f, i));

        const out = await drain(floodGate(gate)(source(envelopes)));

        expect(out).toHaveLength(3);
        expect(out.map(e => (e.frame as unknown as MockVideoFrame).id)).toEqual([1, 2, 3]);
        expect(frames.every(f => !f.closed)).toBe(true);
        expect(gate.skipCount).toBe(0);
    });

    it('closes-and-skips frames while gate is closed; increments skipCount', async () => {
        const stats = createEmptyRecorderStats();
        const gate = new FloodGate();
        gate.close();
        const frames = [new MockVideoFrame(1), new MockVideoFrame(2)];
        const envelopes = frames.map((f, i) => envelope(stats, f, i));

        const out = await drain(floodGate(gate)(source(envelopes)));

        expect(out).toHaveLength(0);
        expect(frames.every(f => f.closed)).toBe(true);
        expect(gate.skipCount).toBe(2);
    });

    it('reflects mid-stream gate transitions', async () => {
        const stats = createEmptyRecorderStats();
        const gate = new FloodGate();
        const frames = [
            new MockVideoFrame(1), new MockVideoFrame(2), new MockVideoFrame(3),
            new MockVideoFrame(4), new MockVideoFrame(5),
        ];

        // Toggle the gate between yields by interleaving the source with
        // an async wrapper that flips state at specific indices.
        async function* gated(): AsyncIterable<CapturedFrame> {
            await Promise.resolve();
            for (let i = 0; i < frames.length; i++) {
                if (i === 2) gate.close();   // closes before yielding frame 3
                if (i === 4) gate.open();    // opens before yielding frame 5
                yield envelope(stats, frames[i], i);
            }
        }

        const out = await drain(floodGate(gate)(gated()));

        // Frames 1, 2 (open) + 5 (open again) survive.
        expect(out.map(e => (e.frame as unknown as MockVideoFrame).id)).toEqual([1, 2, 5]);
        // Frames 3, 4 dropped while closed.
        expect(frames[2].closed).toBe(true);
        expect(frames[3].closed).toBe(true);
        // Survivors are not closed by the operator.
        expect(frames[0].closed).toBe(false);
        expect(frames[1].closed).toBe(false);
        expect(frames[4].closed).toBe(false);
        expect(gate.skipCount).toBe(2);
    });

    it('stats: floodGateSkipPerSec reflects count of skip events in the last second', async () => {
        const stats = createEmptyRecorderStats();
        const gate = new FloodGate();
        gate.close();
        const frames = [
            new MockVideoFrame(1),
            new MockVideoFrame(2),
            new MockVideoFrame(3),
        ];
        const envelopes = frames.map((f, i) => envelope(stats, f, i));

        await drain(floodGate(gate)(source(envelopes)));

        expect(stats.floodGateSkipPerSec).toBe(3);
    });

    it('stats: floodGateSkipPerSec stays 0 while gate is open', async () => {
        const stats = createEmptyRecorderStats();
        const gate = new FloodGate();
        const frames = [new MockVideoFrame(1), new MockVideoFrame(2)];
        const envelopes = frames.map((f, i) => envelope(stats, f, i));

        await drain(floodGate(gate)(source(envelopes)));

        expect(stats.floodGateSkipPerSec).toBe(0);
    });

    it('tolerates VideoFrame.close() throwing', async () => {
        const stats = createEmptyRecorderStats();
        const gate = new FloodGate();
        gate.close();
        const frame = {
            close: () => { throw new Error('synthetic'); },
        } as unknown as VideoFrame;
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

        const out = await drain(floodGate(gate)(source([env])));

        expect(out).toHaveLength(0);
        expect(gate.skipCount).toBe(1);
    });
});

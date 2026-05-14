import { describe, it, expect } from 'vitest';
import { previewTap } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/preview-tap';
import {
    createEmptyRecorderStats,
    type NormalizedFrame,
    type RecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
// ---- Mocks ----------------------------------------------------------------

class MockVideoFrame {
    closed = false;
    public clones: MockVideoFrame[] = [];
    constructor(public id: number) {}
    clone(): VideoFrame {
        const c = new MockVideoFrame(this.id + 1000);
        this.clones.push(c);
        return c as unknown as VideoFrame;
    }
    close(): void { this.closed = true; }
}

class FakeWriter {
    public written: VideoFrame[] = [];
    public writeShouldThrow: Error | null = null;
    public desiredSize: number | null = 1;
    async write(frame: VideoFrame): Promise<void> {
        await Promise.resolve();
        if (this.writeShouldThrow) throw this.writeShouldThrow;
        this.written.push(frame);
    }
}

// ---- Helpers --------------------------------------------------------------

function makeFrames(stats: RecorderStats, count: number): { envelopes: NormalizedFrame[]; frames: MockVideoFrame[] } {
    const frames: MockVideoFrame[] = [];
    const envelopes: NormalizedFrame[] = [];
    for (let i = 0; i < count; i++) {
        const f = new MockVideoFrame(i);
        frames.push(f);
        envelopes.push({
            frame: f as unknown as VideoFrame,
            capturedAt: { timeMs: 100 + i, epoch: 0 },
            index: i,
            dropTrace: [],
            sourceWidth: 1920,
            sourceHeight: 1080,
            forceKeyframe: false,
            rotation: 0,
            stats,
        });
    }
    return { envelopes, frames };
}

function source<T>(items: T[]): AsyncIterable<T> {
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

function makePreviewTap(
    getWriter: () => WritableStreamDefaultWriter<VideoFrame> | null,
    extra: Partial<Parameters<typeof previewTap>[0]> = {},
): ReturnType<typeof previewTap> {
    return previewTap({
        getWriter,
        frameDurationMs: 1,
        nowMs: () => 0,
        sleep: () => Promise.resolve(),
        ...extra,
    });
}

// ---- Tests ----------------------------------------------------------------

describe('previewTap', () => {
    it('getWriter null → no-op (frames pass through unchanged, no clones taken)', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 3);

        const op = makePreviewTap(() => null);
        const out = await drain(op(source(envelopes)));

        expect(out).toHaveLength(3);
        expect(out.map(e => e.index)).toEqual([0, 1, 2]);
        // No clones were created on any frame.
        for (const f of frames) {
            expect(f.clones).toEqual([]);
        }
    });

    it('getWriter non-null → clone() called per item; writer.write() called with the clone', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 3);
        const writer = new FakeWriter();

        const op = makePreviewTap(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const out = await drain(op(source(envelopes)));

        expect(out).toHaveLength(3);
        // Each input frame produced exactly one clone.
        expect(frames.map(f => f.clones.length)).toEqual([1, 1, 1]);
        // The writer received those clones in order.
        expect(writer.written).toHaveLength(3);
        for (let i = 0; i < 3; i++) {
            expect(writer.written[i]).toBe(frames[i].clones[0] as unknown as VideoFrame);
        }
    });

    it('passthrough preserves the original frame and envelope identity', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes } = makeFrames(stats, 2);
        const writer = new FakeWriter();
        const op = makePreviewTap(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);

        const out = await drain(op(source(envelopes)));

        // Same frame references are still on the envelopes after the tap.
        expect(out[0].frame).toBe(envelopes[0].frame);
        expect(out[1].frame).toBe(envelopes[1].frame);
        // Original frames not closed by the tap.
        expect((envelopes[0].frame as unknown as MockVideoFrame).closed).toBe(false);
        expect((envelopes[1].frame as unknown as MockVideoFrame).closed).toBe(false);
    });

    it('getWriter is called once per frame (so the recorder can swap writers mid-stream)', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 4);

        const writerA = new FakeWriter();
        const writerB = new FakeWriter();
        let calls = 0;
        const op = makePreviewTap(() => {
            // First two frames go to A; remaining to B.
            const writer = calls < 2 ? writerA : writerB;
            calls++;
            return writer as unknown as WritableStreamDefaultWriter<VideoFrame>;
        });
        await drain(op(source(envelopes)));

        expect(calls).toBe(4);
        expect(writerA.written.map(f => f as unknown as MockVideoFrame).map(f => f.id)).toEqual([
            frames[0].clones[0].id,
            frames[1].clones[0].id,
        ]);
        expect(writerB.written.map(f => f as unknown as MockVideoFrame).map(f => f.id)).toEqual([
            frames[2].clones[0].id,
            frames[3].clones[0].id,
        ]);
    });

    it('writer.write() rejection closes the clone and is swallowed (frames continue to pass through)', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 2);
        const writer = new FakeWriter();
        writer.writeShouldThrow = new Error('writer closed');

        const op = makePreviewTap(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const out = await drain(op(source(envelopes)));

        expect(out).toHaveLength(2);
        // Clones were created but write() rejected, so the operator closes them.
        for (const f of frames) {
            expect(f.clones).toHaveLength(1);
            expect(f.clones[0].closed).toBe(true);
        }
        // No frames made it to the writer.
        expect(writer.written).toEqual([]);
    });

    it('drops without cloning when the preview writer is backpressured', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 2);
        const writer = new FakeWriter();
        writer.desiredSize = 0;

        const op = makePreviewTap(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const out = await drain(op(source(envelopes)));

        expect(out).toHaveLength(2);
        expect(frames.map(f => f.clones.length)).toEqual([0, 0]);
        expect(writer.written).toEqual([]);
    });

    it('paces bunched frames from capturedAt time', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes } = makeFrames(stats, 3);
        const writer = new FakeWriter();
        let now = 0;
        const sleeps: number[] = [];

        const op = previewTap({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            frameDurationMs: 33,
            nowMs: () => now,
            sleep: async delayMs => {
                await Promise.resolve();
                sleeps.push(delayMs);
                now += delayMs;
            },
        });
        await drain(op(source(envelopes)));

        expect(writer.written).toHaveLength(3);
        expect(sleeps).toEqual([33, 33]);
    });

    it('resets pacing on capture-clock epoch changes', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes } = makeFrames(stats, 3);
        envelopes[2] = {
            ...envelopes[2],
            capturedAt: { timeMs: 0, epoch: 1 },
        };
        const writer = new FakeWriter();
        let now = 0;
        const sleeps: number[] = [];

        const op = previewTap({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            frameDurationMs: 33,
            nowMs: () => now,
            sleep: async delayMs => {
                await Promise.resolve();
                sleeps.push(delayMs);
                now += delayMs;
            },
        });
        await drain(op(source(envelopes)));

        expect(writer.written).toHaveLength(3);
        expect(sleeps).toEqual([33]);
    });

    it('drops preview frames that are already two frame intervals late', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 3);
        const writer = new FakeWriter();
        let calls = 0;

        const op = previewTap({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            frameDurationMs: 33,
            nowMs: () => calls++ === 0 ? 0 : 100,
            sleep: () => Promise.resolve(),
        });
        await drain(op(source(envelopes)));

        expect(writer.written).toHaveLength(1);
        expect(frames.map(f => f.clones.length)).toEqual([1, 0, 0]);
    });
});

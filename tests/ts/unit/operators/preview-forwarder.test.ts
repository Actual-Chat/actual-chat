import { describe, it, expect } from 'vitest';
import { previewForwarder } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/preview-forwarder';
import {
    createEmptyRecorderStats,
    type CapturedBundle,
    type CapturedFrame,
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

// Each bundle's ceiling IS its single layer's frame (non-orphan), so the
// forwarder taps it for preview but does NOT close the original.
function makeFrames(stats: RecorderStats, count: number): { envelopes: CapturedBundle[]; frames: MockVideoFrame[] } {
    const frames: MockVideoFrame[] = [];
    const envelopes: CapturedBundle[] = [];
    for (let i = 0; i < count; i++) {
        const f = new MockVideoFrame(i);
        frames.push(f);
        const layer: CapturedFrame = {
            frame: f as unknown as VideoFrame,
            capturedAt: { timeMs: 100 + i, epoch: 0 },
            index: i,
            dropTrace: [],
            sourceWidth: 1920,
            sourceHeight: 1080,
            forceKeyframe: false,
            rotation: 0,
            stats,
        };
        envelopes.push({
            layers: [layer],
            ceiling: f as unknown as VideoFrame,
            index: i,
            dropTrace: [],
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

async function settlePreviewWork(): Promise<void> {
    for (let i = 0; i < 5; i++)
        await Promise.resolve();
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

function makePreviewForwarder(
    getWriter: () => WritableStreamDefaultWriter<VideoFrame> | null,
    extra: Partial<Parameters<typeof previewForwarder>[0]> = {},
): ReturnType<typeof previewForwarder> {
    return previewForwarder({
        getWriter,
        ...extra,
    });
}

// ---- Tests ----------------------------------------------------------------

describe('previewForwarder', () => {
    it('getWriter null → no-op (frames pass through unchanged, no clones taken)', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 3);

        const op = makePreviewForwarder(() => null);
        const out = await drain(op(source(envelopes)));

        expect(out).toHaveLength(3);
        expect(out.map(e => e.index)).toEqual([0, 1, 2]);
        // No clones were created on any frame.
        for (const f of frames) {
            expect(f.clones).toEqual([]);
        }
    });

    it('getWriter non-null → clone() called per delivered item; writer.write() called with the clone', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 3);
        const writer = new FakeWriter();

        const op = makePreviewForwarder(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const out = await drain(op(source(envelopes)));
        await settlePreviewWork();

        expect(out).toHaveLength(3);
        expect(frames.map(f => f.clones.length)).toEqual([1, 1, 1]);
        expect(writer.written).toHaveLength(3);
        for (let i = 0; i < 3; i++) {
            expect(writer.written[i]).toBe(frames[i].clones[0] as unknown as VideoFrame);
        }
        expect(frames.map(f => f.clones[0].closed)).toEqual([true, true, true]);
    });

    it('passthrough preserves the original frame and envelope identity', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes } = makeFrames(stats, 2);
        const writer = new FakeWriter();
        const op = makePreviewForwarder(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);

        const out = await drain(op(source(envelopes)));
        await settlePreviewWork();

        // Same ceiling references are still on the bundles after the forwarder.
        expect(out[0].ceiling).toBe(envelopes[0].ceiling);
        expect(out[1].ceiling).toBe(envelopes[1].ceiling);
        // Original frames not closed by the forwarder (ceiling is also a layer).
        expect((envelopes[0].ceiling as unknown as MockVideoFrame).closed).toBe(false);
        expect((envelopes[1].ceiling as unknown as MockVideoFrame).closed).toBe(false);
    });

    it('getWriter is called once per frame (so the recorder can swap writers mid-stream)', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 4);

        const writerA = new FakeWriter();
        const writerB = new FakeWriter();
        let calls = 0;
        const op = makePreviewForwarder(() => {
            const writer = calls < 2 ? writerA : writerB;
            calls++;
            return writer as unknown as WritableStreamDefaultWriter<VideoFrame>;
        });
        await drain(op(source(envelopes)));
        await settlePreviewWork();

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

        const op = makePreviewForwarder(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const out = await drain(op(source(envelopes)));
        await settlePreviewWork();

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

        const op = makePreviewForwarder(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const out = await drain(op(source(envelopes)));

        expect(out).toHaveLength(2);
        expect(frames.map(f => f.clones.length)).toEqual([0, 0]);
        expect(writer.written).toEqual([]);
    });

    it('reports frames to the canvas fallback when no writer is available', async () => {
        const stats = createEmptyRecorderStats();
        const { envelopes, frames } = makeFrames(stats, 2);
        const reported: VideoFrame[] = [];

        const op = previewForwarder({
            getWriter: () => null,
            reportFrame: frame => {
                reported.push(frame);
            },
        });
        const out = await drain(op(source(envelopes)));
        await settlePreviewWork();

        expect(out).toHaveLength(2);
        expect(reported).toEqual([
            frames[0].clones[0] as unknown as VideoFrame,
            frames[1].clones[0] as unknown as VideoFrame,
        ]);
        expect(frames.map(f => f.clones[0].closed)).toEqual([true, true]);
    });
});

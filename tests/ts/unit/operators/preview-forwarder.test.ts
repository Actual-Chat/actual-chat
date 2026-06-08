import { describe, it, expect } from 'vitest';
import { createPreviewSink } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/preview-forwarder';

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

function makeSink(
    getWriter: () => WritableStreamDefaultWriter<VideoFrame> | null,
    extra: Partial<Parameters<typeof createPreviewSink>[0]> = {},
) {
    return createPreviewSink({ getWriter, ...extra });
}

// Forward N fresh frames through the sink (rotation 0); returns the originals.
function forwardFrames(
    sink: ReturnType<typeof createPreviewSink>,
    count: number,
): MockVideoFrame[] {
    const frames: MockVideoFrame[] = [];
    for (let i = 0; i < count; i++) {
        const f = new MockVideoFrame(i);
        frames.push(f);
        sink.forward(f as unknown as VideoFrame, 0);
    }
    return frames;
}

async function settlePreviewWork(): Promise<void> {
    for (let i = 0; i < 5; i++)
        await Promise.resolve();
}

// ---- Tests ----------------------------------------------------------------

describe('createPreviewSink', () => {
    it('getWriter null and no reportFrame → no clone taken', () => {
        const sink = makeSink(() => null);
        const frames = forwardFrames(sink, 3);
        for (const f of frames) expect(f.clones).toEqual([]);
    });

    it('getWriter non-null → clone() called per frame; writer.write() called with the clone', async () => {
        const writer = new FakeWriter();
        const sink = makeSink(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const frames = forwardFrames(sink, 3);
        await settlePreviewWork();

        expect(frames.map(f => f.clones.length)).toEqual([1, 1, 1]);
        expect(writer.written).toHaveLength(3);
        for (let i = 0; i < 3; i++)
            expect(writer.written[i]).toBe(frames[i].clones[0] as unknown as VideoFrame);
        expect(frames.map(f => f.clones[0].closed)).toEqual([true, true, true]);
    });

    it('does not close the passed source frame (caller owns it)', async () => {
        const writer = new FakeWriter();
        const sink = makeSink(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const frames = forwardFrames(sink, 2);
        await settlePreviewWork();

        for (const f of frames) expect(f.closed).toBe(false);
    });

    it('getWriter is called once per frame (so the recorder can swap writers mid-stream)', async () => {
        const writerA = new FakeWriter();
        const writerB = new FakeWriter();
        let calls = 0;
        const sink = makeSink(() => {
            const writer = calls < 2 ? writerA : writerB;
            calls++;
            return writer as unknown as WritableStreamDefaultWriter<VideoFrame>;
        });
        const frames = forwardFrames(sink, 4);
        await settlePreviewWork();

        expect(calls).toBe(4);
        expect(writerA.written.map(f => (f as unknown as MockVideoFrame).id)).toEqual([
            frames[0].clones[0].id,
            frames[1].clones[0].id,
        ]);
        expect(writerB.written.map(f => (f as unknown as MockVideoFrame).id)).toEqual([
            frames[2].clones[0].id,
            frames[3].clones[0].id,
        ]);
    });

    it('writer.write() rejection closes the clone and is swallowed', async () => {
        const writer = new FakeWriter();
        writer.writeShouldThrow = new Error('writer closed');
        const sink = makeSink(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const frames = forwardFrames(sink, 2);
        await settlePreviewWork();

        for (const f of frames) {
            expect(f.clones).toHaveLength(1);
            expect(f.clones[0].closed).toBe(true);
        }
        expect(writer.written).toEqual([]);
    });

    it('drops without cloning when the preview writer is backpressured', () => {
        const writer = new FakeWriter();
        writer.desiredSize = 0;
        const sink = makeSink(() => writer as unknown as WritableStreamDefaultWriter<VideoFrame>);
        const frames = forwardFrames(sink, 2);

        expect(frames.map(f => f.clones.length)).toEqual([0, 0]);
        expect(writer.written).toEqual([]);
    });

    it('reports frames to the canvas fallback when no writer is available', async () => {
        const reported: VideoFrame[] = [];
        const sink = createPreviewSink({
            getWriter: () => null,
            reportFrame: frame => { reported.push(frame); },
        });
        const frames = forwardFrames(sink, 2);
        await settlePreviewWork();

        expect(reported).toEqual([
            frames[0].clones[0] as unknown as VideoFrame,
            frames[1].clones[0] as unknown as VideoFrame,
        ]);
        expect(frames.map(f => f.clones[0].closed)).toEqual([true, true]);
    });
});

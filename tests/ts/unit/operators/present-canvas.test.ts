import { describe, it, expect } from 'vitest';
import { count, pipe } from 'ix-ext';
import {
    canvasPresent,
    type CanvasImageInterface,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/present-canvas';
import {
    createEmptyPlaybackStats,
    type DecodedFrame,
    type VideoPlaybackStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
// ---- Mocks ----------------------------------------------------------------

class MockVideoFrame {
    closed = false;
    constructor(public id = 0) {}
    close(): void { this.closed = true; }
    codedWidth = 320;
    codedHeight = 180;
    displayWidth = 320;
    displayHeight = 180;
}

class MockImageBitmap {
    closed = false;
    constructor(public id = 0) {}
    close(): void { this.closed = true; }
}

class FakeCtx implements CanvasImageInterface {
    public canvas = { width: 1280, height: 720 };
    public calls: { image: VideoFrame | ImageBitmap; x: number; y: number; w?: number; h?: number }[] = [];
    drawImage(image: VideoFrame | ImageBitmap, x: number, y: number, w?: number, h?: number): void {
        this.calls.push({ image, x, y, w, h });
    }
}

// ---- Helpers --------------------------------------------------------------

function makeEnvelope(stats: VideoPlaybackStats, id: number, frame?: MockVideoFrame): DecodedFrame {
    const f = frame ?? new MockVideoFrame(id);
    return {
        frame: f as unknown as VideoFrame,
        capturedAt: { timeMs: 100 + id, epoch: 0 },
        arrivedAt: { timeMs: 200 + id, epoch: 0 },
        decodedAt: { timeMs: 300 + id, epoch: 0 },
        spatialLayerId: 0,
        stats,
    };
}

function source(items: DecodedFrame[]): AsyncIterable<DecodedFrame> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

// ---- Tests ----------------------------------------------------------------

describe('canvasPresent', () => {
    it('drawImage is called once per frame at (0, 0); frames are closed; framesPresented increments', async () => {
        const stats = createEmptyPlaybackStats(0);
        const ctx = new FakeCtx();
        const sink = canvasPresent({ getCanvasCtx: () => ctx });
        const frames = Array.from({ length: 5 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, f));

        await count(pipe(source(items), sink));

        expect(ctx.calls).toHaveLength(5);
        for (let i = 0; i < 5; i++) {
            expect(ctx.calls[i].x).toBe(0);
            expect(ctx.calls[i].y).toBe(0);
            expect(ctx.calls[i].w).toBe(320);
            expect(ctx.calls[i].h).toBe(180);
            expect(ctx.calls[i].image).toBe(frames[i] as unknown as VideoFrame);
            expect(frames[i].closed).toBe(true);
        }
        expect(ctx.canvas).toEqual({ width: 320, height: 180 });
        expect(stats.framesPresented).toBe(5);
    });

    it('getCanvasCtx is called exactly once across the run (not per frame)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const ctx = new FakeCtx();
        let calls = 0;
        const sink = canvasPresent({
            getCanvasCtx: () => { calls++; return ctx; },
        });
        const items = Array.from({ length: 4 }, (_, i) => makeEnvelope(stats, i));

        await count(pipe(source(items), sink));

        expect(calls).toBe(1);
        expect(ctx.calls).toHaveLength(4);
    });

    it('Safari path: convertToBitmap is called per frame; bitmap is drawn and closed; frame is closed', async () => {
        const stats = createEmptyPlaybackStats(0);
        const ctx = new FakeCtx();
        const bitmaps: MockImageBitmap[] = [];
        const convertToBitmap = async (frame: VideoFrame): Promise<ImageBitmap> => {
            await Promise.resolve();
            const id = (frame as unknown as MockVideoFrame).id;
            const bm = new MockImageBitmap(id);
            bitmaps.push(bm);
            return bm as unknown as ImageBitmap;
        };
        const sink = canvasPresent({ getCanvasCtx: () => ctx, convertToBitmap });
        const frames = Array.from({ length: 3 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, f));

        await count(pipe(source(items), sink));

        // Bitmap was drawn (not the original VideoFrame).
        expect(ctx.calls).toHaveLength(3);
        for (let i = 0; i < 3; i++) {
            expect(ctx.calls[i].image).toBe(bitmaps[i] as unknown as ImageBitmap);
            expect(bitmaps[i].closed).toBe(true);
            expect(frames[i].closed).toBe(true);
        }
        expect(stats.framesPresented).toBe(3);
    });

    it('resizes backing canvas to display dimensions before drawing', async () => {
        const stats = createEmptyPlaybackStats(0);
        const ctx = new FakeCtx();
        const frame = new MockVideoFrame(1);
        frame.codedWidth = 320;
        frame.codedHeight = 192;
        frame.displayWidth = 320;
        frame.displayHeight = 180;

        const sink = canvasPresent({ getCanvasCtx: () => ctx });
        await count(pipe(source([makeEnvelope(stats, 1, frame)]), sink));

        expect(ctx.canvas).toEqual({ width: 320, height: 180 });
        expect(ctx.calls[0]).toMatchObject({ x: 0, y: 0, w: 320, h: 180 });
    });
});

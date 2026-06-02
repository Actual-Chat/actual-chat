import { describe, it, expect } from 'vitest';
import { count, pipe } from 'ix-ext';
import {
    canvasPresent,
    type CanvasImageInterface,
    type CanvasPresentOptions,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/present-canvas';
import {
    createEmptyPlayerStats,
    type DecodedFrame,
    type PlayerStats,
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

function makeEnvelope(
    stats: PlayerStats,
    id: number,
    capturedAtMs: number,
    frame?: MockVideoFrame,
): DecodedFrame {
    const f = frame ?? new MockVideoFrame(id);
    return {
        frame: f as unknown as VideoFrame,
        capturedAt: { timeMs: capturedAtMs, epoch: 0 },
        arrivedAt: { timeMs: capturedAtMs + 100, epoch: 0 },
        decodedAt: { timeMs: capturedAtMs + 200, epoch: 0 },
        index: id,
        dropTrace: [],
        layerId: 0,
        rotation: 0,
        stats,
    };
}

function source(items: DecodedFrame[]): AsyncIterable<DecodedFrame> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

// nowFn auto-advances 1 s per call so natural-delta pacing never sleeps.
function defaults(extra: { getBufferSpanMs?: () => number; targetSpanMs?: number } = {}): Pick<
    CanvasPresentOptions, 'getBufferSpanMs' | 'targetSpanMs' | 'nowFn' | 'delayFn'
> {
    let t = 0;
    return {
        getBufferSpanMs: extra.getBufferSpanMs ?? ((): number => 0),
        targetSpanMs: extra.targetSpanMs ?? 333,
        nowFn: (): number => { t += 1000; return t; },
        delayFn: (): Promise<void> => Promise.resolve(),
    };
}

// ---- Tests ----------------------------------------------------------------

describe('canvasPresent', () => {
    it('drawImage is called once per frame at (0, 0); frames are closed; framesPresented increments', async () => {
        const stats = createEmptyPlayerStats();
        const ctx = new FakeCtx();
        const sink = canvasPresent({ getCanvasCtx: () => ctx, ...defaults() });
        const frames = Array.from({ length: 5 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

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
        expect(stats.presented).toBe(5);
    });

    it('getCanvasCtx is called exactly once across the run (not per frame)', async () => {
        const stats = createEmptyPlayerStats();
        const ctx = new FakeCtx();
        let calls = 0;
        const sink = canvasPresent({
            getCanvasCtx: () => { calls++; return ctx; },
            ...defaults(),
        });
        const items = Array.from({ length: 4 }, (_, i) => makeEnvelope(stats, i, i * 33));

        await count(pipe(source(items), sink));

        expect(calls).toBe(1);
        expect(ctx.calls).toHaveLength(4);
    });

    it('steady mode: resets pacing on capture epoch change', async () => {
        const stats = createEmptyPlayerStats();
        const ctx = new FakeCtx();
        let now = 1000;
        const delays: number[] = [];
        const sink = canvasPresent({
            getCanvasCtx: () => ctx,
            getBufferSpanMs: (): number => 0,
            targetSpanMs: 333,
            nowFn: (): number => now,
            delayFn: (delayMs: number): Promise<void> => {
                delays.push(delayMs);
                now += delayMs;
                return Promise.resolve();
            },
        });
        const items = [
            makeEnvelope(stats, 0, 0),
            makeEnvelope(stats, 1, 33),
            {
                ...makeEnvelope(stats, 2, 0),
                capturedAt: { timeMs: 0, epoch: 1 },
            },
        ];

        await count(pipe(source(items), sink));

        expect(ctx.calls).toHaveLength(3);
        expect(delays).toHaveLength(1);
        expect(delays[0]).toBeGreaterThan(32);
        expect(delays[0]).toBeLessThan(34);
    });

    it('Safari path: convertToBitmap is called per frame; bitmap is drawn and closed; frame is closed', async () => {
        const stats = createEmptyPlayerStats();
        const ctx = new FakeCtx();
        const bitmaps: MockImageBitmap[] = [];
        const convertToBitmap = async (frame: VideoFrame): Promise<ImageBitmap> => {
            await Promise.resolve();
            const id = (frame as unknown as MockVideoFrame).id;
            const bm = new MockImageBitmap(id);
            bitmaps.push(bm);
            return bm as unknown as ImageBitmap;
        };
        const sink = canvasPresent({ getCanvasCtx: () => ctx, convertToBitmap, ...defaults() });
        const frames = Array.from({ length: 3 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

        await count(pipe(source(items), sink));

        expect(ctx.calls).toHaveLength(3);
        for (let i = 0; i < 3; i++) {
            expect(ctx.calls[i].image).toBe(bitmaps[i] as unknown as ImageBitmap);
            expect(bitmaps[i].closed).toBe(true);
            expect(frames[i].closed).toBe(true);
        }
        expect(stats.presented).toBe(3);
    });

    it('resizes backing canvas to display dimensions before drawing', async () => {
        const stats = createEmptyPlayerStats();
        const ctx = new FakeCtx();
        const frame = new MockVideoFrame(1);
        frame.codedWidth = 320;
        frame.codedHeight = 192;
        frame.displayWidth = 320;
        frame.displayHeight = 180;

        const sink = canvasPresent({ getCanvasCtx: () => ctx, ...defaults() });
        await count(pipe(source([makeEnvelope(stats, 1, 0, frame)]), sink));

        expect(ctx.canvas).toEqual({ width: 320, height: 180 });
        expect(ctx.calls[0]).toMatchObject({ x: 0, y: 0, w: 320, h: 180 });
    });

    it('catch-up overflow (extra > CATCHUP_BUDGET_MS) AND frozen clock → frame closed without draw', async () => {
        const stats = createEmptyPlayerStats();
        const ctx = new FakeCtx();
        // extra = 5000 - 333 = 4667 > CATCHUP_BUDGET_MS (4000); frozen clock keeps
        // every non-first frame within MIN_DURATION_MS → skip-after-decode path.
        const sink = canvasPresent({
            getCanvasCtx: () => ctx,
            getBufferSpanMs: (): number => 5000,
            targetSpanMs: 333,
            nowFn: (): number => 1000,
            delayFn: (): Promise<void> => Promise.resolve(),
            holdMs: 0,
        });
        const frames = Array.from({ length: 3 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

        await count(pipe(source(items), sink));

        expect(ctx.calls).toHaveLength(1);
        expect(stats.presented).toBe(1);
        for (const f of frames) expect(f.closed).toBe(true);
    });

    it('catch-up below budget never skips even when frames land within MIN_DURATION_MS', async () => {
        const stats = createEmptyPlayerStats();
        const ctx = new FakeCtx();
        // extra = 167, well under CATCHUP_BUDGET_MS (4000) → no skip.
        const sink = canvasPresent({
            getCanvasCtx: () => ctx,
            getBufferSpanMs: (): number => 500,
            targetSpanMs: 333,
            nowFn: (): number => 1000,
            delayFn: (): Promise<void> => Promise.resolve(),
        });
        const frames = Array.from({ length: 4 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

        await count(pipe(source(items), sink));

        expect(ctx.calls).toHaveLength(4);
        expect(stats.presented).toBe(4);
    });

    it('stats: presentSkipRatio rises when frames take the skip branch', async () => {
        const stats = createEmptyPlayerStats();
        const ctx = new FakeCtx();
        // extra = 5000 - 333 = 4667 > CATCHUP_BUDGET_MS (4000); frozen clock keeps
        // every non-first frame within MIN_DURATION_MS → skip-after-decode path.
        const sink = canvasPresent({
            getCanvasCtx: () => ctx,
            getBufferSpanMs: (): number => 5000,
            targetSpanMs: 333,
            nowFn: (): number => 1000,
            delayFn: (): Promise<void> => Promise.resolve(),
            holdMs: 0,
        });
        const items = Array.from({ length: 5 }, (_, i) => makeEnvelope(stats, i, i * 33));

        await count(pipe(source(items), sink));

        // Frame 0 draws (lastWriteAt=null → no skip branch); frames 1..4 skip.
        // Sample sequence: 0, 1, 1, 1, 1 → EMA (alpha=0.1) above 0.3.
        expect(stats.presented).toBe(1);
        expect(stats.presentSkipRatio).toBeGreaterThan(0.3);
        expect(stats.presentSkipRatio).toBeLessThanOrEqual(1);
    });
});

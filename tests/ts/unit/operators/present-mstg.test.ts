import { describe, it, expect } from 'vitest';
import { count, pipe } from 'ix-ext';
import { mstgPresent } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/present-mstg';
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

interface PendingWrite {
    frame: VideoFrame;
    resolve: () => void;
    reject: (e: unknown) => void;
}

/**
 * Fake writer that records each `write()` call AND lets the test
 * control when the resulting promise resolves. Mirrors the behavior
 * of `MediaStreamTrackGenerator.writable.getWriter()` closely enough
 * to exercise the sink's single-slot replace logic.
 */
class FakeWriter {
    public written: VideoFrame[] = [];
    public pending: PendingWrite[] = [];
    /** When false, write() resolves synchronously — useful for the
     *  "all frames written" sanity test. When true, write() returns a
     *  pending promise that the test resolves manually via
     *  `flushNext()` to simulate slow downstream consumers. */
    public manualMode = false;

    write(frame: VideoFrame): Promise<void> {
        this.written.push(frame);
        if (!this.manualMode) return Promise.resolve();
        return new Promise<void>((resolve, reject) => {
            this.pending.push({ frame, resolve, reject });
        });
    }

    /** Resolves the oldest pending write. */
    async flushNext(): Promise<void> {
        const next = this.pending.shift();
        if (!next) throw new Error('no pending write to flush');
        next.resolve();
        // Yield once so the sink's microtask continuation runs.
        await Promise.resolve();
        await Promise.resolve();
    }

    async rejectNext(e: unknown = new Error('write failed')): Promise<void> {
        const next = this.pending.shift();
        if (!next) throw new Error('no pending write to reject');
        next.reject(e);
        await Promise.resolve();
        await Promise.resolve();
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
        layerId: 0,
        stats,
    };
}

/**
 * Source backed by a controllable channel — the test pushes envelopes
 * one at a time, then closes when done. Lets us interleave "send a
 * frame" with "flush a write" and observe the sink's single-slot
 * replace logic step by step.
 */
interface ControllableSource<T> {
    push: (item: T) => void;
    close: () => void;
    seg: AsyncIterable<T>;
}

function controllableSource<T>(): ControllableSource<T> {
    const queue: T[] = [];
    let resolveNext: ((v: IteratorResult<T>) => void) | null = null;
    let closed = false;

    const push = (item: T): void => {
        if (resolveNext) {
            const r = resolveNext;
            resolveNext = null;
            r({ value: item, done: false });
        } else {
            queue.push(item);
        }
    };
    const close = (): void => {
        closed = true;
        if (resolveNext) {
            const r = resolveNext;
            resolveNext = null;
            r({ value: undefined as unknown as T, done: true });
        }
    };

    const seg: AsyncIterable<T> = {
        [Symbol.asyncIterator](): AsyncIterator<T> {
            return {
                next(): Promise<IteratorResult<T>> {
                    if (queue.length > 0) {
                        return Promise.resolve({ value: queue.shift()!, done: false });
                    }
                    if (closed) return Promise.resolve({ value: undefined as unknown as T, done: true });
                    return new Promise<IteratorResult<T>>(resolve => { resolveNext = resolve; });
                },
            };
        },
    };

    return { push, close, seg };
}

function staticSource<T>(items: T[]): AsyncIterable<T> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

// Tick the microtask queue a few times so the sink can advance.
async function tick(times = 4): Promise<void> {
    for (let i = 0; i < times; i++) await Promise.resolve();
}

// ---- Tests ----------------------------------------------------------------

describe('mstgPresent', () => {
    it('writes every frame and increments framesPresented (slow upstream)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        const sink = mstgPresent({ getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame> });
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1), new MockVideoFrame(2)];
        const items = frames.map((f, i) => makeEnvelope(stats, i, f));

        await count(pipe(staticSource(items), sink));

        // Slow upstream + auto-resolving writes → all 3 frames make it through.
        expect(writer.written).toHaveLength(3);
        expect(stats.framesPresented).toBe(3);
        // Each written frame was the original (not closed before write).
        expect(writer.written[0]).toBe(frames[0] as unknown as VideoFrame);
        expect(writer.written[2]).toBe(frames[2] as unknown as VideoFrame);
        expect(frames.map(f => f.closed)).toEqual([true, true, true]);
    });

    it('single-slot replace: when frames outpace writes, intermediate frames are dropped (and closed)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({ getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame> });
        const ch = controllableSource<DecodedFrame>();
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1), new MockVideoFrame(2), new MockVideoFrame(3)];

        const run = count(pipe(ch.seg, sink));

        // Push frame 0. Sink takes it, calls writer.write (which stays pending).
        ch.push(makeEnvelope(stats, 0, frames[0]));
        await tick();
        expect(writer.written).toEqual([frames[0] as unknown as VideoFrame]);
        expect(writer.pending).toHaveLength(1);

        // Push 1 and 2 while frame 0's write is in flight. Frame 1 is closed
        // when frame 2 displaces it.
        ch.push(makeEnvelope(stats, 1, frames[1]));
        await tick();
        ch.push(makeEnvelope(stats, 2, frames[2]));
        await tick();
        expect(frames[1].closed).toBe(true);
        expect(frames[2].closed).toBe(false);
        // Writer hasn't seen 1 or 2 yet — frame 0's write is still pending.
        expect(writer.written).toHaveLength(1);

        // Resolve frame 0's write. Sink advances to frame 2 (frame 1 is gone).
        await writer.flushNext();
        await tick();
        expect(writer.written).toHaveLength(2);
        expect(writer.written[1]).toBe(frames[2] as unknown as VideoFrame);

        // Push frame 3 while frame 2 is in flight, then close source.
        ch.push(makeEnvelope(stats, 3, frames[3]));
        await tick();
        await writer.flushNext();
        await tick();
        ch.close();
        await writer.flushNext();
        await run;

        // Frames 0, 2, 3 made it; frame 1 was dropped. framesPresented == 3.
        expect(writer.written.map(f => (f as unknown as MockVideoFrame).id)).toEqual([0, 2, 3]);
        expect(stats.framesPresented).toBe(3);
        expect(stats.framesDroppedAtPresenter).toBe(1);
        expect(frames.map(f => f.closed)).toEqual([true, true, true, true]);
    });

    it('write rejection is reflected in framesDroppedAtPresenter', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({ getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame> });
        const ch = controllableSource<DecodedFrame>();
        const frame = new MockVideoFrame(0);

        const run = count(pipe(ch.seg, sink));
        ch.push(makeEnvelope(stats, 0, frame));
        await tick();
        await writer.rejectNext(new Error('writer broke'));

        ch.push(makeEnvelope(stats, 1, new MockVideoFrame(1)));
        await expect(run).rejects.toThrow('writer broke');
        expect(stats.framesPresented).toBe(0);
        expect(stats.framesDroppedAtPresenter).toBeGreaterThanOrEqual(1);
    });

    it('framesPresented increments only after writer.write resolves (not on enqueue)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({ getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame> });
        const ch = controllableSource<DecodedFrame>();
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1)];
        const run = count(pipe(ch.seg, sink));

        ch.push(makeEnvelope(stats, 0, frames[0]));
        await tick();
        // Write is in flight — counter must NOT have advanced yet.
        expect(stats.framesPresented).toBe(0);

        await writer.flushNext();
        await tick();
        expect(stats.framesPresented).toBe(1);

        ch.push(makeEnvelope(stats, 1, frames[1]));
        await tick();
        expect(stats.framesPresented).toBe(1);

        ch.close();
        await writer.flushNext();
        await run;
        expect(stats.framesPresented).toBe(2);
    });

    it('upstream completes while a frame sits in pending → final frame is presented (not leaked)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({ getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame> });
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1)];
        const ch = controllableSource<DecodedFrame>();

        const run = count(pipe(ch.seg, sink));
        ch.push(makeEnvelope(stats, 0, frames[0]));
        await tick();
        ch.push(makeEnvelope(stats, 1, frames[1]));
        await tick();
        ch.close();
        // Frame 0 is in flight; frame 1 is pending. Resolve frame 0; sink
        // should pick frame 1 next.
        await writer.flushNext();
        await tick();
        await writer.flushNext();
        await run;

        expect(writer.written).toHaveLength(2);
        expect(stats.framesPresented).toBe(2);
        expect(frames.map(f => f.closed)).toEqual([true, true]);
    });

    it('upstream throws → pending frame is closed in finally (no GPU leak)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({ getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame> });
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1)];

        const seg: AsyncIterable<DecodedFrame> = (async function* () {
            await Promise.resolve();
            yield makeEnvelope(stats, 0, frames[0]);
            yield makeEnvelope(stats, 1, frames[1]);
            throw new Error('upstream blew up');
        })();

        const run = count(pipe(seg, sink));
        await tick(8);
        // Drain so the writer accepts frame 0; frame 1 stays pending when
        // upstream throws.
        await writer.flushNext();
        // Now allow the throw to propagate.
        let caught: unknown = null;
        try {
            await run;
        } catch (e) {
            caught = e;
        }
        expect((caught as Error).message).toBe('upstream blew up');
        // Frame 1 was the pending slot when the throw fired → must be closed.
        expect(frames[1].closed).toBe(true);
    });

    it('upstream throws without waiting for a stuck write', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({ getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame> });
        const frame = new MockVideoFrame(0);

        const seg: AsyncIterable<DecodedFrame> = (async function* () {
            await Promise.resolve();
            yield makeEnvelope(stats, 0, frame);
            throw new Error('upstream stopped');
        })();

        const run = count(pipe(seg, sink));
        await tick();

        await expect(run).rejects.toThrow('upstream stopped');
        expect(frame.closed).toBe(true);
        expect(writer.pending).toHaveLength(1);
    });

    it('propagates a previous write failure before accepting a new frame', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({ getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame> });
        const ch = controllableSource<DecodedFrame>();
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1)];

        const run = count(pipe(ch.seg, sink));
        ch.push(makeEnvelope(stats, 0, frames[0]));
        await tick();
        await writer.rejectNext(new Error('writer broke'));

        ch.push(makeEnvelope(stats, 1, frames[1]));

        await expect(run).rejects.toThrow('writer broke');
        expect(frames[0].closed).toBe(true);
        expect(frames[1].closed).toBe(true);
    });
});

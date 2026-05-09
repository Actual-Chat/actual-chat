import { describe, it, expect } from 'vitest';
import { count, pipe } from 'ix-ext';
import {
    mstgPresent,
    type MstgPresentOptions,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/present-mstg';
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

class FakeWriter {
    public written: VideoFrame[] = [];
    public pending: PendingWrite[] = [];
    /** When false, write() resolves synchronously. When true, write() returns
     *  a pending promise that the test resolves manually via `flushNext()`. */
    public manualMode = false;

    write(frame: VideoFrame): Promise<void> {
        this.written.push(frame);
        if (!this.manualMode) return Promise.resolve();
        return new Promise<void>((resolve, reject) => {
            this.pending.push({ frame, resolve, reject });
        });
    }

    async flushNext(): Promise<void> {
        const next = this.pending.shift();
        if (!next) throw new Error('no pending write to flush');
        next.resolve();
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

async function tick(times = 4): Promise<void> {
    for (let i = 0; i < times; i++) await Promise.resolve();
}

interface FakeClock {
    now: number;
    nowFn: () => number;
    delays: number[];
    delayFn: (ms: number) => Promise<void>;
    advance: (ms: number) => void;
}

/** Manual virtual clock: nowFn returns whatever `now` is set to,
 *  delayFn records the requested delay AND advances the clock. */
function fakeClock(start = 0): FakeClock {
    const c: FakeClock = {
        now: start,
        nowFn: () => c.now,
        delays: [],
        delayFn: async (ms: number) => {
            c.delays.push(ms);
            c.now += ms;
            await Promise.resolve();
        },
        advance: (ms: number) => { c.now += ms; },
    };
    return c;
}

function defaults(extra: Partial<MstgPresentOptions> = {}): Pick<
    MstgPresentOptions, 'getBufferSpanMs' | 'targetSpanMs' | 'nowFn' | 'delayFn'
> {
    let t = 0;
    return {
        getBufferSpanMs: extra.getBufferSpanMs ?? ((): number => 0),
        targetSpanMs: extra.targetSpanMs ?? 333,
        // Default nowFn auto-advances 1s per call so the period cap never
        // imposes a sleep — keeps non-pacing tests fast.
        nowFn: extra.nowFn ?? ((): number => { t += 1000; return t; }),
        delayFn: extra.delayFn ?? ((): Promise<void> => Promise.resolve()),
    };
}

// ---- Tests ----------------------------------------------------------------

describe('mstgPresent', () => {
    it('writes every frame and increments framesPresented (in-budget, fast clock)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1), new MockVideoFrame(2)];
        const items = frames.map((f, i) => makeEnvelope(stats, i, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(3);
        expect(stats.framesPresented).toBe(3);
        expect(writer.written[0]).toBe(frames[0] as unknown as VideoFrame);
        expect(writer.written[2]).toBe(frames[2] as unknown as VideoFrame);
        expect(frames.map(f => f.closed)).toEqual([true, true, true]);
    });

    it('paces writes via delayFn when nextPresentMs > now (frozen-clock case)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        const clock = fakeClock(1000);
        // Use a controlled clock that doesn't auto-advance — every frame
        // fires at the same wallclock, so the cap forces a delayFn call.
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 0,
            targetSpanMs: 333,
            nowFn: clock.nowFn,
            delayFn: clock.delayFn,
        });
        const frames = Array.from({ length: 4 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, f));

        await count(pipe(staticSource(items), sink));

        // First frame: nextPresentMs starts null → set to now, no delay.
        // Subsequent frames: nextPresentMs is in the future → sleep PRESENT_PERIOD_MS.
        expect(writer.written).toHaveLength(4);
        expect(stats.framesPresented).toBe(4);
        // Three sleeps for frames 1, 2, 3 — all of length ~16.67 ms.
        expect(clock.delays).toHaveLength(3);
        for (const d of clock.delays) {
            expect(d).toBeGreaterThan(16);
            expect(d).toBeLessThan(17);
        }
    });

    it('catch-up skip: out-of-budget AND frozen clock → all but the first are closed without write', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        // extra = 2000 - 333 = 1667 > 1000 → out of budget. Frozen clock
        // means every frame after the first lands within the period →
        // skip-after-decode path.
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 2000,
            targetSpanMs: 333,
            nowFn: (): number => 1000,
            delayFn: (): Promise<void> => Promise.resolve(),
        });
        const frames = Array.from({ length: 5 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, f));

        await count(pipe(staticSource(items), sink));

        // Only the first frame (lastPresentMs starts at -Infinity → first
        // is never within-period) writes; the rest are skipped.
        expect(writer.written).toHaveLength(1);
        expect(stats.framesPresented).toBe(1);
        expect(stats.framesDroppedAtPresenter).toBe(4);
        for (const f of frames) expect(f.closed).toBe(true);
    });

    it('catch-up skip: in-budget never skips even when frames land within the period', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        // extra = 0 < catchup budget → in-budget mode → never skip,
        // pace via delayFn instead.
        const clock = fakeClock(0);
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 0,
            targetSpanMs: 333,
            nowFn: clock.nowFn,
            delayFn: clock.delayFn,
        });
        const frames = Array.from({ length: 4 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(4);
        expect(stats.framesPresented).toBe(4);
        expect(stats.framesDroppedAtPresenter).toBe(0);
    });

    it('write rejection bumps framesDroppedAtPresenter and propagates the error', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const ch = controllableSource<DecodedFrame>();
        const frame = new MockVideoFrame(0);

        const run = count(pipe(ch.seg, sink));
        ch.push(makeEnvelope(stats, 0, frame));
        await tick();
        await writer.rejectNext(new Error('writer broke'));

        await expect(run).rejects.toThrow('writer broke');
        expect(stats.framesPresented).toBe(0);
        expect(stats.framesDroppedAtPresenter).toBe(1);
        expect(frame.closed).toBe(true);
    });

    it('framesPresented increments only after writer.write resolves (not on enqueue)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
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

    it('upstream throws → final frame is closed in finally (no GPU leak)', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1)];

        const seg: AsyncIterable<DecodedFrame> = (async function* () {
            await Promise.resolve();
            yield makeEnvelope(stats, 0, frames[0]);
            yield makeEnvelope(stats, 1, frames[1]);
            throw new Error('upstream blew up');
        })();

        await expect(count(pipe(seg, sink))).rejects.toThrow('upstream blew up');
        // Both yielded frames went through the present loop; both closed.
        expect(frames[0].closed).toBe(true);
        expect(frames[1].closed).toBe(true);
    });

    it('getWriter is called once and reused across frames', async () => {
        const stats = createEmptyPlaybackStats(0);
        const writer = new FakeWriter();
        let writerCalls = 0;
        const sink = mstgPresent({
            getWriter: () => {
                writerCalls++;
                return writer as unknown as WritableStreamDefaultWriter<VideoFrame>;
            },
            ...defaults(),
        });
        const items = Array.from({ length: 4 }, (_, i) => makeEnvelope(stats, i));

        await count(pipe(staticSource(items), sink));

        expect(writerCalls).toBe(1);
        expect(writer.written).toHaveLength(4);
    });
});

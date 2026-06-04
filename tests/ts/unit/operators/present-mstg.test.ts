import { describe, it, expect } from 'vitest';
import { count, pipe } from 'ix-ext';
import {
    mstgPresent,
    type MstgPresentOptions,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/present-mstg';
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
    /** Synchronous backpressure signal: >0 → room (gate skips ready). */
    public desiredSize: number | null = 1;

    private _ready: Promise<void> = Promise.resolve();
    private _resolveReady: (() => void) | null = null;
    private _rejectReady: ((e: unknown) => void) | null = null;

    get ready(): Promise<void> { return this._ready; }

    blockReady(): void {
        this._ready = new Promise<void>((resolve, reject) => {
            this._resolveReady = resolve;
            this._rejectReady = reject;
        });
    }
    unblockReady(): void {
        this._resolveReady?.();
        this._resolveReady = null;
        this._rejectReady = null;
        this._ready = Promise.resolve();
    }
    failReady(e: unknown = new Error('ready failed')): void {
        this._rejectReady?.(e);
        this._resolveReady = null;
        this._rejectReady = null;
    }

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
        serverArrivedAtUnixMs: 0,
        decodedAt: { timeMs: capturedAtMs + 200, epoch: 0 },
        index: id,
        dropTrace: [],
        layerId: 0,
        rotation: 0,
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
        // nowFn auto-advances 1 s per call so natural-delta pacing never sleeps.
        nowFn: extra.nowFn ?? ((): number => { t += 1000; return t; }),
        delayFn: extra.delayFn ?? ((): Promise<void> => Promise.resolve()),
    };
}

// ---- Tests ----------------------------------------------------------------

describe('mstgPresent', () => {
    it('writes every frame and increments framesPresented (steady, fast clock)', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1), new MockVideoFrame(2)];
        const items = frames.map((f, i) => makeEnvelope(stats, i, 100 + i * 33, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(3);
        expect(stats.presented).toBe(3);
        expect(writer.written[0]).toBe(frames[0] as unknown as VideoFrame);
        expect(writer.written[2]).toBe(frames[2] as unknown as VideoFrame);
        expect(frames.map(f => f.closed)).toEqual([true, true, true]);
    });

    it('steady mode: paces writes by naturalDelta (capture-time gap)', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const clock = fakeClock(1000);
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 0,        // extra = 0 → steady mode
            targetSpanMs: 333,
            nowFn: clock.nowFn,
            delayFn: clock.delayFn,
        });
        // 33 ms apart in capture time (30 fps), inside [MIN, MAX] → no clamping.
        const frames = Array.from({ length: 4 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, 1000 + i * 33, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(4);
        expect(stats.presented).toBe(4);
        expect(clock.delays).toHaveLength(3);
        for (const d of clock.delays) {
            expect(d).toBeGreaterThan(32);
            expect(d).toBeLessThan(34);
        }
    });

    it('steady mode: resets pacing on capture epoch change', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const clock = fakeClock(1000);
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 0,
            targetSpanMs: 333,
            nowFn: clock.nowFn,
            delayFn: clock.delayFn,
        });
        const items = [
            makeEnvelope(stats, 0, 0),
            makeEnvelope(stats, 1, 33),
            {
                ...makeEnvelope(stats, 2, 0),
                capturedAt: { timeMs: 0, epoch: 1 },
            },
        ];

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(3);
        expect(clock.delays).toHaveLength(1);
        expect(clock.delays[0]).toBeGreaterThan(32);
        expect(clock.delays[0]).toBeLessThan(34);
    });

    it('steady mode: clamps duration up to MIN_DURATION_MS for source above 120 fps', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const clock = fakeClock(0);
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 0,
            targetSpanMs: 333,
            nowFn: clock.nowFn,
            delayFn: clock.delayFn,
        });
        // 3 ms apart (~333 fps) → naturalDelta is below the MIN_DURATION_MS floor.
        const frames = Array.from({ length: 4 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 3, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(4);
        // Each delay clamped up to MIN_DURATION_MS (1000/120 ≈ 8.33 ms).
        expect(clock.delays).toHaveLength(3);
        for (const d of clock.delays) {
            expect(d).toBeGreaterThan(8);
            expect(d).toBeLessThan(9);
        }
    });

    it('steady mode: clamps duration down to MAX_DURATION_MS for source below 10 fps', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const clock = fakeClock(0);
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 0,
            targetSpanMs: 333,
            nowFn: clock.nowFn,
            delayFn: clock.delayFn,
        });
        // 200 ms apart (5 fps) → naturalDelta exceeds MAX_DURATION_MS.
        const frames = Array.from({ length: 3 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 200, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(3);
        // Each delay clamped down to MAX_DURATION_MS (1000/10 = 100 ms).
        expect(clock.delays).toHaveLength(2);
        for (const d of clock.delays) expect(d).toBeCloseTo(100, 5);
    });

    it('catch-up mode (extra > 0, ≤ budget): writes every frame at MIN_DURATION_MS cadence', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const clock = fakeClock(0);
        // extra = 167, well under CATCHUP_BUDGET_MS (4000).
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 500,
            targetSpanMs: 333,
            nowFn: clock.nowFn,
            delayFn: clock.delayFn,
        });
        const frames = Array.from({ length: 4 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(4);
        expect(stats.presented).toBe(4);
        expect(clock.delays).toHaveLength(3);
        for (const d of clock.delays) {
            expect(d).toBeGreaterThan(8);
            expect(d).toBeLessThan(9);
        }
    });

    it('catch-up overflow (extra > CATCHUP_BUDGET_MS) AND frozen clock → skip-after-decode', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        // extra = 5000 - 333 = 4667 > CATCHUP_BUDGET_MS (4000); frozen clock keeps
        // every non-first frame within MIN_DURATION_MS → skip-after-decode path.
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 5000,
            targetSpanMs: 333,
            nowFn: (): number => 1000,
            delayFn: (): Promise<void> => Promise.resolve(),
            holdMs: 0,
        });
        const frames = Array.from({ length: 5 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(1);
        expect(stats.presented).toBe(1);
        for (const f of frames) expect(f.closed).toBe(true);
    });

    it('catch-up below budget never skips even when frames land within MIN_DURATION_MS', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const clock = fakeClock(0);
        // extra = 167, well under CATCHUP_BUDGET_MS (4000) → no skip.
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 500,
            targetSpanMs: 333,
            nowFn: clock.nowFn,
            delayFn: clock.delayFn,
        });
        const frames = Array.from({ length: 4 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(4);
        expect(stats.presented).toBe(4);
    });

    it('buffer-span meter: a one-frame span spike does NOT trigger skip', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        // Span sits at target (extra=0, steady) except a single spike to 5000 on
        // frame 2. Frozen clock keeps every frame within MIN_DURATION_MS, so the
        // raw spike would trip skip-mode — but the windowed-min meter holds the
        // low floor, so no frame is dropped.
        const spans = [333, 333, 5000, 333, 333];
        let spanCall = 0;
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => spans[spanCall++] ?? 333,
            targetSpanMs: 333,
            nowFn: (): number => 1000,
            delayFn: (): Promise<void> => Promise.resolve(),
        });
        const frames = Array.from({ length: 5 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i * 33, f));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(5);
        expect(stats.presented).toBe(5);
        for (const f of frames) expect(f.closed).toBe(true);
    });

    it('write rejection bumps framesDroppedAtPresenter and propagates the error', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const ch = controllableSource<DecodedFrame>();
        const frame = new MockVideoFrame(0);

        const run = count(pipe(ch.seg, sink));
        ch.push(makeEnvelope(stats, 0, 0, frame));
        await tick();
        await writer.rejectNext(new Error('writer broke'));

        await expect(run).rejects.toThrow('writer broke');
        expect(stats.presented).toBe(0);
        expect(frame.closed).toBe(true);
    });

    it('framesPresented increments only after writer.write resolves (not on enqueue)', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        writer.manualMode = true;
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const ch = controllableSource<DecodedFrame>();
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1)];
        const run = count(pipe(ch.seg, sink));

        ch.push(makeEnvelope(stats, 0, 0, frames[0]));
        await tick();
        // Write is in flight — counter must NOT have advanced yet.
        expect(stats.presented).toBe(0);

        await writer.flushNext();
        await tick();
        expect(stats.presented).toBe(1);

        ch.push(makeEnvelope(stats, 1, 33, frames[1]));
        await tick();
        expect(stats.presented).toBe(1);

        ch.close();
        await writer.flushNext();
        await run;
        expect(stats.presented).toBe(2);
    });

    it('upstream throws → final frame is closed in finally (no GPU leak)', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const frames = [new MockVideoFrame(0), new MockVideoFrame(1)];

        const seg: AsyncIterable<DecodedFrame> = (async function* () {
            await Promise.resolve();
            yield makeEnvelope(stats, 0, 0, frames[0]);
            yield makeEnvelope(stats, 1, 33, frames[1]);
            throw new Error('upstream blew up');
        })();

        await expect(count(pipe(seg, sink))).rejects.toThrow('upstream blew up');
        // Both yielded frames went through the present loop; both closed.
        expect(frames[0].closed).toBe(true);
        expect(frames[1].closed).toBe(true);
    });

    it('getWriter is called once and reused across frames', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        let writerCalls = 0;
        const sink = mstgPresent({
            getWriter: () => {
                writerCalls++;
                return writer as unknown as WritableStreamDefaultWriter<VideoFrame>;
            },
            ...defaults(),
        });
        const items = Array.from({ length: 4 }, (_, i) => makeEnvelope(stats, i, i * 33));

        await count(pipe(staticSource(items), sink));

        expect(writerCalls).toBe(1);
        expect(writer.written).toHaveLength(4);
    });

    it('stats: presentSkipRatio is 0 when no frames take the catch-up skip branch', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const clock = fakeClock(0);
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 0,
            targetSpanMs: 333,
            nowFn: clock.nowFn,
            delayFn: clock.delayFn,
        });
        const items = Array.from({ length: 4 }, (_, i) => makeEnvelope(stats, i, i * 33));

        await count(pipe(staticSource(items), sink));

        expect(stats.presented).toBe(4);
        // 4 samples of 0 → EMA stays 0.
        expect(stats.presentSkipRatio).toBe(0);
    });

    it('stats: presentSkipRatio rises when frames take the skip branch', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        // Same setup as the existing skip-after-decode test.
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 5000,
            targetSpanMs: 333,
            nowFn: (): number => 1000,
            delayFn: (): Promise<void> => Promise.resolve(),
            holdMs: 0,
        });
        const items = Array.from({ length: 5 }, (_, i) => makeEnvelope(stats, i, i * 33));

        await count(pipe(staticSource(items), sink));

        // Frame 0 writes (lastWriteAt=null → no skip branch); frames 1..4 skip.
        // Sample sequence: 0, 1, 1, 1, 1 → EMA (alpha=0.1) above 0.3.
        expect(stats.presented).toBe(1);
        expect(stats.presentSkipRatio).toBeGreaterThan(0.3);
        expect(stats.presentSkipRatio).toBeLessThanOrEqual(1);
    });

    // ---- Skip-mode hysteresis (holdMs) ------------------------------------

    // now advances < MIN_DURATION_MS (8.33) per frame so frames stay
    // skip-eligible; the 1 ms meter windows make the smoothed span == raw span.
    function advancingClock(stepMs: number): () => number {
        let now = 0;
        return (): number => { const v = now; now += stepMs; return v; };
    }

    it('hysteresis: skip engages only after sustained overflow exceeds holdMs', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 5000,           // sustained overflow
            targetSpanMs: 333,
            nowFn: advancingClock(8),
            delayFn: (): Promise<void> => Promise.resolve(),
            holdMs: 50,
        });
        const frames = Array.from({ length: 10 }, (_, i) => new MockVideoFrame(i));
        const items = frames.map((f, i) => makeEnvelope(stats, i, i, f));

        await count(pipe(staticSource(items), sink));

        // now = 8·idx: frames 0..6 (now ≤ 48 < 50) present; skip engages at frame 7 (now 56).
        for (let i = 0; i <= 6; i++)
            expect(writer.written).toContain(frames[i] as unknown as VideoFrame);
        expect(writer.written).not.toContain(frames[7] as unknown as VideoFrame);
    });

    it('hysteresis: a sub-holdMs overflow blip drops zero frames', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        // Overflow for 4 frames (~24 ms < holdMs 50), then clears → never engages.
        const spans = [5000, 5000, 5000, 5000, 333, 333, 333, 333];
        let si = 0;
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => spans[si++] ?? 333,
            targetSpanMs: 333,
            nowFn: advancingClock(8),
            delayFn: (): Promise<void> => Promise.resolve(),
            holdMs: 50,
        });
        const items = Array.from({ length: 8 }, (_, i) => makeEnvelope(stats, i, i));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(8);
        expect(stats.presented).toBe(8);
    });

    it('hysteresis: an interrupted overflow restarts the hold timer (no skip)', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        // Overflow ~40 ms, one frame dips below budget, overflow resumes: neither
        // run reaches holdMs (50), so the timer restart prevents any skip.
        const spans = [5000, 5000, 5000, 5000, 5000, 333, 5000, 5000, 5000, 5000, 5000, 5000, 5000, 5000];
        let si = 0;
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => spans[si++] ?? 333,
            targetSpanMs: 333,
            nowFn: advancingClock(8),
            delayFn: (): Promise<void> => Promise.resolve(),
            holdMs: 50,
        });
        const items = Array.from({ length: 14 }, (_, i) => makeEnvelope(stats, i, i));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(14);
        expect(stats.presented).toBe(14);
    });

    // ---- Backpressure (writer.desiredSize / writer.ready) -----------------

    it('backpressure: awaits writer.ready when desiredSize<=0, then writes', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        writer.desiredSize = 0;
        writer.blockReady();
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const ch = controllableSource<DecodedFrame>();
        const frame = new MockVideoFrame(0);
        const run = count(pipe(ch.seg, sink));

        ch.push(makeEnvelope(stats, 0, 0, frame));
        await tick();
        // ready is pending → the write must not have happened yet.
        expect(writer.written).toHaveLength(0);
        expect(stats.presented).toBe(0);

        writer.unblockReady();
        await tick();
        expect(writer.written).toHaveLength(1);
        expect(stats.presented).toBe(1);

        ch.close();
        await run;
    });

    it('no backpressure wait when desiredSize>0 even if ready is pending', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        // Room available; a pending ready must be ignored (gate skipped).
        writer.desiredSize = 4;
        writer.blockReady();
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const ch = controllableSource<DecodedFrame>();
        const frame = new MockVideoFrame(0);
        const run = count(pipe(ch.seg, sink));

        ch.push(makeEnvelope(stats, 0, 0, frame));
        ch.close();
        // If the gate had awaited the (still-pending) ready, run would hang and
        // this await would time out — completing proves the gate was skipped.
        await run;
        expect(writer.written).toHaveLength(1);
        expect(stats.presented).toBe(1);
    });

    it('ready rejection propagates like a write failure', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        writer.desiredSize = 0;
        writer.blockReady();
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            ...defaults(),
        });
        const ch = controllableSource<DecodedFrame>();
        const frame = new MockVideoFrame(0);
        const run = count(pipe(ch.seg, sink));

        ch.push(makeEnvelope(stats, 0, 0, frame));
        await tick();
        writer.failReady(new Error('ready broke'));

        await expect(run).rejects.toThrow('ready broke');
        expect(stats.presented).toBe(0);
        expect(writer.written).toHaveLength(0);
        expect(frame.closed).toBe(true);
    });

    it('hysteresis: rate-limit still presents engaged frames that are behind schedule', async () => {
        const stats = createEmptyPlayerStats();
        const writer = new FakeWriter();
        // holdMs 0 → skip engages immediately, but now advances 20 ms/frame
        // (> MIN_DURATION_MS), so every frame is behind schedule → rate-limit
        // presents them all despite skip being engaged.
        const sink = mstgPresent({
            getWriter: () => writer as unknown as WritableStreamDefaultWriter<VideoFrame>,
            getBufferSpanMs: (): number => 5000,
            targetSpanMs: 333,
            nowFn: advancingClock(20),
            delayFn: (): Promise<void> => Promise.resolve(),
            holdMs: 0,
        });
        const items = Array.from({ length: 6 }, (_, i) => makeEnvelope(stats, i, i));

        await count(pipe(staticSource(items), sink));

        expect(writer.written).toHaveLength(6);
        expect(stats.presented).toBe(6);
    });
});

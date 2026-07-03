import { describe, it, expect } from 'vitest';
import { keepAlive, type KeepAliveOptions } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/keep-alive';
import {
    createEmptyRecorderStats,
    type CapturedFrame,
    type RecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

class MockVideoFrame {
    closed = false;
    clones: MockVideoFrame[] = [];
    constructor(public id: number, public timestamp = 0) {}
    close(): void { this.closed = true; }
    clone(): MockVideoFrame {
        if (this.closed) throw new Error('clone of closed frame');
        const c = new MockVideoFrame(this.id, this.timestamp);
        this.clones.push(c);
        return c;
    }
}

interface FakeTimer {
    cb: () => void;
    atMs: number;
    id: number;
    cleared: boolean;
    fired: boolean;
}

class FakeClock {
    nowMs = 0;
    timers: FakeTimer[] = [];
    private nextId = 1;

    now = (): number => this.nowMs;

    setTimeout = (cb: () => void, ms: number): unknown => {
        const t: FakeTimer = { cb, atMs: this.nowMs + ms, id: this.nextId++, cleared: false, fired: false };
        this.timers.push(t);
        return t.id;
    };

    clearTimeout = (handle: unknown): void => {
        const t = this.timers.find(x => x.id === handle);
        if (t) t.cleared = true;
    };

    get liveTimers(): FakeTimer[] {
        return this.timers.filter(t => !t.cleared && !t.fired);
    }

    async advance(ms: number): Promise<void> {
        this.nowMs += ms;
        for (const t of this.timers) {
            if (!t.cleared && !t.fired && t.atMs <= this.nowMs) {
                t.fired = true;
                t.cb();
            }
        }
        await flush();
    }

    // Fires the earliest live timer without moving the clock — a "stale wake".
    async fireNext(): Promise<void> {
        const t = this.liveTimers.sort((a, b) => a.atMs - b.atMs).at(0);
        if (t) {
            t.fired = true;
            t.cb();
        }
        await flush();
    }
}

class ManualSource implements AsyncIterable<CapturedFrame> {
    returnCalled = false;
    private waiting: { resolve: (r: IteratorResult<CapturedFrame>) => void; reject: (e: Error) => void }[] = [];
    private buffered: ({ result: IteratorResult<CapturedFrame> } | { error: Error })[] = [];

    push(value: CapturedFrame): void {
        this.emit({ done: false, value });
    }

    end(): void {
        this.emit({ done: true, value: undefined });
    }

    throwError(error: Error): void {
        const w = this.waiting.shift();
        if (w) w.reject(error);
        else this.buffered.push({ error });
    }

    private emit(result: IteratorResult<CapturedFrame>): void {
        const w = this.waiting.shift();
        if (w) w.resolve(result);
        else this.buffered.push({ result });
    }

    [Symbol.asyncIterator](): AsyncIterator<CapturedFrame> {
        return {
            next: (): Promise<IteratorResult<CapturedFrame>> => {
                const b = this.buffered.shift();
                if (b)
                    return 'error' in b ? Promise.reject(b.error) : Promise.resolve(b.result);
                return new Promise((resolve, reject) => this.waiting.push({ resolve, reject }));
            },
            return: (): Promise<IteratorResult<CapturedFrame>> => {
                this.returnCalled = true;
                return Promise.resolve({ done: true, value: undefined });
            },
        };
    }
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

async function flush(): Promise<void> {
    for (let i = 0; i < 3; i++)
        await new Promise<void>(resolve => setTimeout(resolve, 0));
}

interface Harness {
    clock: FakeClock;
    source: ManualSource;
    stats: RecorderStats;
    out: CapturedFrame[];
    whenDrained: Promise<void>;
    outFrame: (i: number) => MockVideoFrame;
}

function createHarness(opts?: Partial<KeepAliveOptions>): Harness {
    const clock = new FakeClock();
    const source = new ManualSource();
    const stats = createEmptyRecorderStats();
    const op = keepAlive({
        periodMs: 1000,
        nowFn: clock.now,
        setTimeoutFn: clock.setTimeout,
        clearTimeoutFn: clock.clearTimeout,
        cloneFrame: (src, timestampUs) => {
            const clone = (src as unknown as MockVideoFrame).clone();
            clone.timestamp = timestampUs;
            return clone as unknown as VideoFrame;
        },
        ...opts,
    });
    const out: CapturedFrame[] = [];
    const whenDrained = (async () => {
        for await (const e of op(source))
            out.push(e);
    })();
    return {
        clock, source, stats, out, whenDrained,
        outFrame: i => out[i].frame as unknown as MockVideoFrame,
    };
}

describe('keepAlive operator', () => {
    it('periodMs <= 0 is identity', async () => {
        const stats = createEmptyRecorderStats();
        const frames = [new MockVideoFrame(1), new MockVideoFrame(2)];
        const envelopes = frames.map((f, i) => envelope(stats, f, i));
        const clock = new FakeClock();
        const op = keepAlive({ periodMs: 0, setTimeoutFn: clock.setTimeout, clearTimeoutFn: clock.clearTimeout });

        const out: CapturedFrame[] = [];
        for await (const e of op((async function* () {
            await Promise.resolve();
            yield* envelopes;
        })()))
            out.push(e);

        expect(out).toEqual(envelopes);
        expect(frames.every(f => f.clones.length === 0)).toBe(true);
        expect(clock.timers).toHaveLength(0);
    });

    it('passes frames through, retaining a clone of the last one', async () => {
        const h = createHarness();
        const f1 = new MockVideoFrame(1, 100);
        const f2 = new MockVideoFrame(2, 200);

        h.source.push(envelope(h.stats, f1, 0));
        await flush();
        h.source.push(envelope(h.stats, f2, 1));
        await flush();

        expect(h.out.map(e => e.index)).toEqual([0, 1]);
        expect(h.outFrame(0)).toBe(f1);
        expect(f1.clones).toHaveLength(1);
        expect(f1.clones[0].closed).toBe(true); // replaced by f2's clone
        expect(f2.clones[0].closed).toBe(false);

        h.source.end();
        await h.whenDrained;
        expect(f2.clones[0].closed).toBe(true);
    });

    it('injects a re-emitted clone after one idle period', async () => {
        const h = createHarness();
        const f1 = new MockVideoFrame(1, 5000);
        h.source.push(envelope(h.stats, f1, 0));
        await flush();

        await h.clock.advance(1000);

        expect(h.out).toHaveLength(2);
        expect(h.out[0].isKeepAlive).toBeUndefined();
        const injected = h.out[1];
        expect(injected.index).toBe(1);
        expect(injected.forceKeyframe).toBe(false);
        expect(injected.isKeepAlive).toBe(true);
        expect(injected.dropTrace).toEqual([]);
        expect(injected.capturedAt).toEqual({ timeMs: 0, epoch: 0 });
        expect(h.outFrame(1).id).toBe(1);
        expect(h.outFrame(1).timestamp).toBe(5000 + 1_000_000);
        expect(h.stats.keepAliveFramesInjected).toBe(1);

        h.source.end();
        await h.whenDrained;
    });

    it('repeats injections at the period cadence with increasing index/timestamp', async () => {
        const h = createHarness();
        h.source.push(envelope(h.stats, new MockVideoFrame(1, 0), 0));
        await flush();

        await h.clock.advance(1000);
        await h.clock.advance(1000);

        expect(h.out.map(e => e.index)).toEqual([0, 1, 2]);
        expect(h.outFrame(1).timestamp).toBe(1_000_000);
        expect(h.outFrame(2).timestamp).toBe(2_000_000);
        expect(h.stats.keepAliveFramesInjected).toBe(2);

        h.source.end();
        await h.whenDrained;
    });

    it('remaps real-frame indices past injected ones, preserving upstream gaps', async () => {
        const h = createHarness();
        h.source.push(envelope(h.stats, new MockVideoFrame(1, 0), 0));
        await flush();
        await h.clock.advance(1000);
        await h.clock.advance(1000);

        h.source.push(envelope(h.stats, new MockVideoFrame(2, 0), 1));
        await flush();
        // Upstream gap (flood-gate drops): 2..4 skipped.
        h.source.push(envelope(h.stats, new MockVideoFrame(3, 0), 5));
        await flush();

        expect(h.out.map(e => e.index)).toEqual([0, 1, 2, 3, 7]);

        h.source.end();
        await h.whenDrained;
    });

    it('does not inject before the first real frame', async () => {
        const h = createHarness();

        await h.clock.advance(5000);

        expect(h.out).toHaveLength(0);
        expect(h.clock.timers).toHaveLength(0);

        h.source.end();
        await h.whenDrained;
    });

    it('skips injection while the gate is closed, resumes when it reopens', async () => {
        let gateOpen = false;
        const h = createHarness({ isGateOpen: () => gateOpen });
        h.source.push(envelope(h.stats, new MockVideoFrame(1, 0), 0));
        await flush();

        await h.clock.advance(1000);
        expect(h.out).toHaveLength(1);
        expect(h.stats.keepAliveFramesInjected).toBe(0);
        expect(h.clock.liveTimers).toHaveLength(1); // re-armed

        gateOpen = true;
        await h.clock.advance(1000);
        expect(h.out).toHaveLength(2);
        expect(h.stats.keepAliveFramesInjected).toBe(1);

        h.source.end();
        await h.whenDrained;
    });

    it('ignores a stale timer wake and re-arms', async () => {
        const h = createHarness();
        h.source.push(envelope(h.stats, new MockVideoFrame(1, 0), 0));
        await flush();

        await h.clock.fireNext(); // fires with no time elapsed

        expect(h.out).toHaveLength(1);
        expect(h.stats.keepAliveFramesInjected).toBe(0);
        expect(h.clock.liveTimers).toHaveLength(1);

        h.source.end();
        await h.whenDrained;
    });

    it('closes the retained clone when downstream stops early', async () => {
        const clock = new FakeClock();
        const source = new ManualSource();
        const stats = createEmptyRecorderStats();
        const op = keepAlive({
            periodMs: 1000,
            nowFn: clock.now,
            setTimeoutFn: clock.setTimeout,
            clearTimeoutFn: clock.clearTimeout,
        });
        const f1 = new MockVideoFrame(1, 0);
        source.push(envelope(stats, f1, 0));

        for await (const e of op(source)) {
            expect(e.index).toBe(0);
            break;
        }
        await flush();

        expect(f1.clones[0].closed).toBe(true);
        expect(source.returnCalled).toBe(true);
    });

    it('closes the retained clone when upstream throws', async () => {
        const h = createHarness();
        const f1 = new MockVideoFrame(1, 0);
        h.source.push(envelope(h.stats, f1, 0));
        await flush();

        h.source.throwError(new Error('synthetic'));

        await expect(h.whenDrained).rejects.toThrow('synthetic');
        expect(f1.clones[0].closed).toBe(true);
    });

    it('tolerates clone() throwing — no injection until the next frame', async () => {
        const h = createHarness();
        const f1 = new MockVideoFrame(1, 0);
        f1.closed = true; // clone() throws
        h.source.push(envelope(h.stats, f1, 0));
        await flush();

        expect(h.out).toHaveLength(1);
        await h.clock.advance(5000);
        expect(h.out).toHaveLength(1); // nothing retained, nothing injected

        const f2 = new MockVideoFrame(2, 0);
        h.source.push(envelope(h.stats, f2, 1));
        await flush();
        await h.clock.advance(1000);
        expect(h.out).toHaveLength(3);
        expect(h.stats.keepAliveFramesInjected).toBe(1);

        h.source.end();
        await h.whenDrained;
    });
});

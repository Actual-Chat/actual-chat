import { describe, it, expect } from 'vitest';
import { count } from 'ix-ext';
import { pacedEncodedBuffer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encoded-buffer';
import { EncodedFrameBuffer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/encoded-frame-buffer';
import {
    createEmptyPlaybackStats,
    type ArrivedChunk,
    type VideoPlaybackStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
// ---- Helpers --------------------------------------------------------------

class MockEncodedVideoChunk {
    closed = false;
    close(): void { this.closed = true; }
}

function mkChunk(opts: {
    timeMs: number;
    arrivedAtMs: number;
    isKeyFrame: boolean;
    epoch?: number;
    stats: VideoPlaybackStats;
}): ArrivedChunk {
    return {
        chunk: new MockEncodedVideoChunk() as unknown as EncodedVideoChunk,
        arrivedAt: { timeMs: opts.arrivedAtMs, epoch: 0 },
        capturedAt: { timeMs: opts.timeMs, epoch: opts.epoch ?? 0 },
        isKeyFrame: opts.isKeyFrame,
        layerId: 0,
        width: 640,
        height: 480,
        rawByteLength: 1024,
        stats: opts.stats,
    };
}

interface ManualSource {
    seg: AsyncIterable<ArrivedChunk>;
    push: (c: ArrivedChunk) => void;
    end: () => void;
}

/** Synthetic source that emits chunks one at a time from an external queue. */
function manualSource(): ManualSource {
    const queue: ArrivedChunk[] = [];
    const waiters: ((v: IteratorResult<ArrivedChunk>) => void)[] = [];
    let ended = false;

    const push = (c: ArrivedChunk): void => {
        const w = waiters.shift();
        if (w) w({ value: c, done: false });
        else queue.push(c);
    };
    const end = (): void => {
        ended = true;
        while (waiters.length > 0) {
            const w = waiters.shift();
            if (w) w({ value: undefined as unknown as ArrivedChunk, done: true });
        }
    };
    const seg: AsyncIterable<ArrivedChunk> = {
        [Symbol.asyncIterator](): AsyncIterator<ArrivedChunk> {
            return {
                next(): Promise<IteratorResult<ArrivedChunk>> {
                    if (queue.length > 0) {
                        return Promise.resolve({ value: queue.shift()!, done: false });
                    }
                    if (ended) {
                        return Promise.resolve({ value: undefined as unknown as ArrivedChunk, done: true });
                    }
                    return new Promise(resolve => waiters.push(resolve));
                },
                return(): Promise<IteratorResult<ArrivedChunk>> {
                    ended = true;
                    return Promise.resolve({ value: undefined as unknown as ArrivedChunk, done: true });
                },
            };
        },
    };
    return { seg, push, end };
}

interface FakeTimer {
    cb: () => void;
    delayMs: number;
    canceled: boolean;
}

interface FakeClock {
    now: () => number;
    setTimeoutFn: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn: (h: unknown) => void;
    advance: (ms: number) => Promise<void>;
    setTo: (ms: number) => Promise<void>;
}

function makeFakeClock(): FakeClock {
    let t = 0;
    const timers: FakeTimer[] = [];
    const advance = async (ms: number): Promise<void> => {
        t += ms;
        // Repeatedly fire pending timers and flush microtasks until the
        // system reaches a fixed point (no new timers scheduled and
        // microtask queue idle). This matches the typical "tick the
        // clock, settle the system" expectation while remaining robust
        // to async hops between operator yields, consumer for-await
        // iterations, and inner Promise.race continuations.
        for (let pass = 0; pass < 20; pass++) {
            let firedAny = false;
            for (const timer of timers.splice(0)) {
                if (!timer.canceled) {
                    timer.cb();
                    firedAny = true;
                }
            }
            for (let i = 0; i < 8; i++) await Promise.resolve();
            if (!firedAny && timers.length === 0) break;
        }
    };
    return {
        now: (): number => t,
        setTimeoutFn: (cb: () => void, ms: number): unknown => {
            const timer: FakeTimer = { cb, delayMs: ms, canceled: false };
            timers.push(timer);
            return timer;
        },
        clearTimeoutFn: (h: unknown): void => {
            const timer = h as FakeTimer;
            timer.canceled = true;
        },
        advance,
        setTo: (ms: number): Promise<void> => advance(ms - t),
    };
}

// ---- Tests ----------------------------------------------------------------

describe('pacedEncodedBuffer', () => {
    it('drops deltas while in reset state and increments stats.chunksDroppedAtBuffer', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlaybackStats(0);
        const clock = makeFakeClock();
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 100, now: clock.now });
        const op = pacedEncodedBuffer({
            buffer,
            abortSignal: ac.signal,
            now: clock.now,
            setTimeoutFn: clock.setTimeoutFn,
            clearTimeoutFn: clock.clearTimeoutFn,
        });

        const src = manualSource();
        const collected: ArrivedChunk[] = [];
        const driverSeg: AsyncIterable<ArrivedChunk> = (async function* () {
            for await (const item of op(src.seg)) {
                collected.push(item);
                yield item;
            }
        })();
        const runPromise = count(driverSeg);

        // Push a delta while reset → drop, no yield, no buffered chunk.
        src.push(mkChunk({ timeMs: 100, arrivedAtMs: 0, isKeyFrame: false, stats }));
        await clock.advance(0);
        await clock.advance(20); // fires inner pacing timer if any
        expect(stats.chunksDroppedAtBuffer).toBe(1);
        expect(collected).toHaveLength(0);
        expect(buffer.isReset()).toBe(true);

        src.end();
        ac.abort();
        await runPromise;
    });

    it('yields chunks once the buffer reaches target span and dueAt arrives', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlaybackStats(0);
        const clock = makeFakeClock();
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 100, now: clock.now });
        const op = pacedEncodedBuffer({
            buffer,
            abortSignal: ac.signal,
            now: clock.now,
            setTimeoutFn: clock.setTimeoutFn,
            clearTimeoutFn: clock.clearTimeoutFn,
        });

        const src = manualSource();
        const collected: ArrivedChunk[] = [];
        const driverSeg: AsyncIterable<ArrivedChunk> = (async function* () {
            for await (const item of op(src.seg)) {
                collected.push(item);
                yield item;
            }
        })();
        const runPromise = count(driverSeg);

        // Push 8 chunks at 33 ms cadence so the residual span stays
        // ≥ targetSpanMs (100) even after the front is pulled — that
        // way the buffer stays "ready" for chunk 1's pacing turn.
        const cs: ArrivedChunk[] = [];
        for (let i = 0; i < 8; i++) {
            cs.push(mkChunk({
                timeMs: i * 33,
                arrivedAtMs: i * 33,
                isKeyFrame: i === 0,
                stats,
            }));
        }
        for (const c of cs) src.push(c);
        await clock.advance(0);

        // We're at t=0 → still waiting on dueAt 100.
        expect(collected.length).toBe(0);

        // Advance to t=100 → cs[0] (arrivedAt=0) is now due.
        await clock.setTo(100);
        expect(collected.length).toBeGreaterThanOrEqual(1);
        expect(collected[0]).toBe(cs[0]);

        // Advance to t=133 → cs[1] due.
        await clock.setTo(133);
        expect(collected.length).toBeGreaterThanOrEqual(2);
        expect(collected[1]).toBe(cs[1]);

        src.end();
        ac.abort();
        await runPromise;
    });

    it('honors reset state: deltas drop, then a fresh keyframe re-arms', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlaybackStats(0);
        const clock = makeFakeClock();
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 50, now: clock.now });
        const op = pacedEncodedBuffer({
            buffer,
            abortSignal: ac.signal,
            now: clock.now,
            setTimeoutFn: clock.setTimeoutFn,
            clearTimeoutFn: clock.clearTimeoutFn,
        });

        const src = manualSource();
        const collected: ArrivedChunk[] = [];
        const driverSeg: AsyncIterable<ArrivedChunk> = (async function* () {
            for await (const item of op(src.seg)) {
                collected.push(item);
                yield item;
            }
        })();
        const runPromise = count(driverSeg);

        // Three deltas dropped (reset state).
        for (let i = 0; i < 3; i++) {
            src.push(mkChunk({ timeMs: 33 * i, arrivedAtMs: 33 * i, isKeyFrame: false, stats }));
        }
        await clock.advance(0);
        expect(stats.chunksDroppedAtBuffer).toBe(3);
        expect(buffer.isReset()).toBe(true);

        // Keyframe arms the buffer.
        src.push(mkChunk({ timeMs: 100, arrivedAtMs: 100, isKeyFrame: true, stats }));
        await clock.advance(0);
        expect(buffer.isReset()).toBe(false);
        expect(buffer.count()).toBe(1);

        src.end();
        ac.abort();
        await runPromise;
    });

    it('stop() unwinds the operator promptly even with chunks pending', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlaybackStats(0);
        const clock = makeFakeClock();
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 1_000_000, now: clock.now }); // unreachable
        const op = pacedEncodedBuffer({
            buffer,
            abortSignal: ac.signal,
            now: clock.now,
            setTimeoutFn: clock.setTimeoutFn,
            clearTimeoutFn: clock.clearTimeoutFn,
        });

        const src = manualSource();
        const collected: ArrivedChunk[] = [];
        const driverSeg: AsyncIterable<ArrivedChunk> = (async function* () {
            for await (const item of op(src.seg)) {
                collected.push(item);
                yield item;
            }
        })();
        const runPromise = count(driverSeg);

        src.push(mkChunk({ timeMs: 0, arrivedAtMs: 0, isKeyFrame: true, stats }));
        const pendingChunk = mkChunk({ timeMs: 33, arrivedAtMs: 33, isKeyFrame: false, stats });
        src.push(pendingChunk);
        await clock.advance(0);

        // Nothing yielded — span unreachable.
        expect(collected.length).toBe(0);
        ac.abort();
        // Allow the stopped Promise.race branches to win.
        await clock.advance(0);
        // runStream rejects with signal.reason on abort; callers filter.
        await runPromise.catch(() => { /* expected */ });
        expect(collected.length).toBe(0);
        expect(buffer.count()).toBe(0);
        expect((pendingChunk.chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
    });

    it('drains as many due chunks as the cushion permits in one tick', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlaybackStats(0);
        const clock = makeFakeClock();
        // Target 50 ms cushion. The buffer keeps ≥ 50 ms of content
        // beyond whatever it has yielded — so on a single tick it
        // drains the prefix while the residual span stays ≥ target.
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 50, now: clock.now });
        const op = pacedEncodedBuffer({
            buffer,
            abortSignal: ac.signal,
            now: clock.now,
            setTimeoutFn: clock.setTimeoutFn,
            clearTimeoutFn: clock.clearTimeoutFn,
        });

        const src = manualSource();
        const collected: ArrivedChunk[] = [];
        const driverSeg: AsyncIterable<ArrivedChunk> = (async function* () {
            for await (const item of op(src.seg)) {
                collected.push(item);
                yield item;
            }
        })();
        const runPromise = count(driverSeg);

        // 5 chunks at 33 ms cadence: capturedAt = 0,33,66,99,132.
        // After draining chunk 0: residual span = 132 - 33 = 99 (≥ 50)
        // After draining chunk 1: residual span = 132 - 66 = 66 (≥ 50)
        // After draining chunk 2: residual span = 132 - 99 = 33 (< 50) → stop
        // So one tick should yield exactly chunks [0, 1, 2].
        const cs = [
            mkChunk({ timeMs: 0, arrivedAtMs: 0, isKeyFrame: true, stats }),
            mkChunk({ timeMs: 33, arrivedAtMs: 33, isKeyFrame: false, stats }),
            mkChunk({ timeMs: 66, arrivedAtMs: 66, isKeyFrame: false, stats }),
            mkChunk({ timeMs: 99, arrivedAtMs: 99, isKeyFrame: false, stats }),
            mkChunk({ timeMs: 132, arrivedAtMs: 132, isKeyFrame: false, stats }),
        ];
        for (const c of cs) src.push(c);
        await clock.advance(0);

        // Move to t=200 — every chunk's dueAt has elapsed.
        await clock.setTo(200);
        await clock.advance(0);

        expect(collected).toEqual([cs[0], cs[1], cs[2]]);
        expect(buffer.count()).toBe(2);

        src.end();
        ac.abort();
        await runPromise;
        expect(buffer.count()).toBe(0);
        expect((cs[3].chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
        expect((cs[4].chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
    });
});

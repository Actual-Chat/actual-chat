import { describe, it, expect } from 'vitest';
import { count } from 'ix-ext';
import { pacedEncodedBuffer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encoded-buffer';
import { EncodedFrameBuffer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/encoded-frame-buffer';
import {
    createEmptyPlayerStats,
    type ArrivedChunk,
    type PlayerStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
import { FrameDropStage } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-drop-trace';
// ---- Helpers --------------------------------------------------------------

class MockEncodedVideoChunk {
    closed = false;
    close(): void { this.closed = true; }
}

function mkChunk(opts: {
    timeMs: number;
    isKeyFrame: boolean;
    epoch?: number;
    stats: PlayerStats;
}): ArrivedChunk {
    return {
        chunk: new MockEncodedVideoChunk() as unknown as EncodedVideoChunk,
        arrivedAt: { timeMs: opts.timeMs, epoch: 0 },
        capturedAt: { timeMs: opts.timeMs, epoch: opts.epoch ?? 0 },
        index: 0,
        dropTrace: [],
        serverArrivedAtUnixMs: 0,
        isKeyFrame: opts.isKeyFrame,
        layerId: 0,
        width: 640,
        height: 480,
        rawByteLength: 1024,
        rotation: 0,
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

async function tick(times = 50): Promise<void> {
    for (let i = 0; i < times; i++) await Promise.resolve();
}

// ---- Tests ----------------------------------------------------------------

describe('pacedEncodedBuffer', () => {
    it('yields chunks once the buffer reaches target span', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlayerStats();
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 100, frameDurationMs: 33.333 });
        const op = pacedEncodedBuffer({ buffer, abortSignal: ac.signal });

        const src = manualSource();
        const collected: ArrivedChunk[] = [];
        const driverSeg: AsyncIterable<ArrivedChunk> = (async function* () {
            for await (const item of op(src.seg)) {
                collected.push(item);
                yield item;
            }
        })();
        const runPromise = count(driverSeg);

        // 8 chunks at 33 ms cadence: span = 231 ≥ 100. After draining
        // chunk 0, residual = 231 - 33 = 198 ≥ 100; chunk 1 also drains.
        // After chunk 1: residual = 165 ≥ 100; chunk 2 drains too. Etc.
        const cs: ArrivedChunk[] = [];
        for (let i = 0; i < 8; i++) {
            cs.push(mkChunk({ timeMs: i * 33, isKeyFrame: i === 0, stats }));
        }
        for (const c of cs) src.push(c);
        await tick();

        // First few in FIFO order; exact count depends on scheduling but
        // the prefix that satisfies span ≥ target on every step gets out.
        expect(collected.length).toBeGreaterThanOrEqual(1);
        expect(collected[0]).toBe(cs[0]);
        if (collected.length >= 2) expect(collected[1]).toBe(cs[1]);

        src.end();
        ac.abort();
        await runPromise;
    });

    it('honors reset state: deltas drop, then a fresh keyframe re-arms', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlayerStats();
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 50, frameDurationMs: 33.333 });
        const op = pacedEncodedBuffer({ buffer, abortSignal: ac.signal });

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
            src.push(mkChunk({ timeMs: 33 * i, isKeyFrame: false, stats }));
        }
        await tick();
        expect(buffer.isReset()).toBe(true);

        // Keyframe arms the buffer (single chunk → span 0 < target so no yield).
        src.push(mkChunk({ timeMs: 100, isKeyFrame: true, stats }));
        await tick();
        expect(buffer.isReset()).toBe(false);
        expect(buffer.count()).toBe(1);

        src.end();
        ac.abort();
        await runPromise;
    });

    it('stop() unwinds the operator promptly even with chunks pending', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlayerStats();
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 1_000_000, frameDurationMs: 33.333 }); // unreachable
        const op = pacedEncodedBuffer({ buffer, abortSignal: ac.signal });

        const src = manualSource();
        const collected: ArrivedChunk[] = [];
        const driverSeg: AsyncIterable<ArrivedChunk> = (async function* () {
            for await (const item of op(src.seg)) {
                collected.push(item);
                yield item;
            }
        })();
        const runPromise = count(driverSeg);

        src.push(mkChunk({ timeMs: 0, isKeyFrame: true, stats }));
        const pendingChunk = mkChunk({ timeMs: 33, isKeyFrame: false, stats });
        src.push(pendingChunk);
        await tick();

        // Nothing yielded — span unreachable.
        expect(collected.length).toBe(0);
        ac.abort();
        await tick();
        // runStream rejects with signal.reason on abort; callers filter.
        await runPromise.catch(() => { /* expected */ });
        expect(collected.length).toBe(0);
        expect(buffer.count()).toBe(0);
        expect((pendingChunk.chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
    });

    it('closes a chunk that arrives via in-flight next() after abort', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlayerStats();
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 1_000, frameDurationMs: 33.333 });
        const op = pacedEncodedBuffer({ buffer, abortSignal: ac.signal });

        // Cooperative source: return() resolves any pending next() with done=true,
        // but a chunk pushed *between* abort and return() races into the in-flight
        // promise and must be closed by the operator.
        const queue: ArrivedChunk[] = [];
        const waiters: ((v: IteratorResult<ArrivedChunk>) => void)[] = [];
        let ended = false;
        const seg: AsyncIterable<ArrivedChunk> = {
            [Symbol.asyncIterator](): AsyncIterator<ArrivedChunk> {
                return {
                    next(): Promise<IteratorResult<ArrivedChunk>> {
                        if (queue.length > 0)
                            return Promise.resolve({ value: queue.shift()!, done: false });
                        if (ended)
                            return Promise.resolve({ value: undefined as unknown as ArrivedChunk, done: true });

                        return new Promise(resolve => waiters.push(resolve));
                    },
                    return(): Promise<IteratorResult<ArrivedChunk>> {
                        ended = true;
                        while (waiters.length > 0)
                            waiters.shift()!({ value: undefined as unknown as ArrivedChunk, done: true });
                        return Promise.resolve({ value: undefined as unknown as ArrivedChunk, done: true });
                    },
                };
            },
        };
        const collected: ArrivedChunk[] = [];
        const driverSeg: AsyncIterable<ArrivedChunk> = (async function* () {
            for await (const item of op(seg)) {
                collected.push(item);
                yield item;
            }
        })();
        const runPromise = count(driverSeg);

        // Let the operator install a waiter for next().
        await tick();
        const inflightChunk = mkChunk({ timeMs: 0, isKeyFrame: true, stats });

        // Abort, then deliver a chunk into the pending next() before return() lands.
        ac.abort();
        const w = waiters.shift();
        if (w) w({ value: inflightChunk, done: false });
        await tick();
        await runPromise.catch(() => { /* expected */ });

        // Settle the .then(...) tail handler that closes the in-flight chunk.
        for (let i = 0; i < 8; i++) await Promise.resolve();
        expect((inflightChunk.chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
    });

    it('drains as many chunks as the cushion permits in one batch', async () => {
        const ac = new AbortController();
        const stats = createEmptyPlayerStats();
        // Target 50 ms cushion. 5 chunks @ 33 ms cadence: spanMs = 132+33.333 = 165.333.
        // After draining chunk 0: residual = 99+33.333 = 132.333 (≥ 50) → pull.
        // After draining chunk 1: residual = 66+33.333 = 99.333  (≥ 50) → pull.
        // After draining chunk 2: residual = 33+33.333 = 66.333  (≥ 50) → pull.
        // After draining chunk 3: residual = 0+33.333  = 33.333  (< 50) → stop.
        // So one batch should yield exactly chunks [0, 1, 2, 3].
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 50, frameDurationMs: 33.333 });
        const op = pacedEncodedBuffer({ buffer, abortSignal: ac.signal });

        const src = manualSource();
        const collected: ArrivedChunk[] = [];
        const driverSeg: AsyncIterable<ArrivedChunk> = (async function* () {
            for await (const item of op(src.seg)) {
                collected.push(item);
                yield item;
            }
        })();
        const runPromise = count(driverSeg);

        const cs = [
            mkChunk({ timeMs: 0, isKeyFrame: true, stats }),
            mkChunk({ timeMs: 33, isKeyFrame: false, stats }),
            mkChunk({ timeMs: 66, isKeyFrame: false, stats }),
            mkChunk({ timeMs: 99, isKeyFrame: false, stats }),
            mkChunk({ timeMs: 132, isKeyFrame: false, stats }),
        ];
        for (const c of cs) src.push(c);
        await tick();

        expect(collected).toEqual([cs[0], cs[1], cs[2], cs[3]]);
        expect(buffer.count()).toBe(1);

        src.end();
        ac.abort();
        await runPromise;
        expect(buffer.count()).toBe(0);
        expect((cs[4].chunk as unknown as MockEncodedVideoChunk).closed).toBe(true);
    });
});

describe('EncodedFrameBuffer skip-to-live', () => {
    it('drops the stale backlog to the newest buffered keyframe past threshold', () => {
        const stats = createEmptyPlayerStats();
        const buffer = new EncodedFrameBuffer({
            targetSpanMs: 100,
            frameDurationMs: 33.333,
            stats,
            skipToLiveSpanMs: 300,
            nowFn: () => 0,
        });

        buffer.push(mkChunk({ timeMs: 0, isKeyFrame: true, stats }));      // 0
        for (let i = 1; i <= 8; i++)                                       // 1..8 deltas
            buffer.push(mkChunk({ timeMs: 33 * i, isKeyFrame: false, stats }));
        const kf2 = mkChunk({ timeMs: 297, isKeyFrame: true, stats });
        const head0 = buffer.count();
        buffer.push(kf2);                                                  // newer keyframe
        // span now 297 + 33.333 = 330.3 > 300 → skip drops [0..8] (9 chunks)
        // before kf2; kf2 becomes the head.
        buffer.push(mkChunk({ timeMs: 330, isKeyFrame: false, stats }));

        expect(head0).toBe(9);
        expect(buffer.count()).toBe(2); // kf2 + the trailing delta
        expect(stats.dropTrace.get(FrameDropStage.ReceiverSkipToLive)).toBe(9);
    });

    it('does not skip when there is no newer keyframe to land on', () => {
        const stats = createEmptyPlayerStats();
        const buffer = new EncodedFrameBuffer({
            targetSpanMs: 100,
            frameDurationMs: 33.333,
            stats,
            skipToLiveSpanMs: 300,
            nowFn: () => 0,
        });

        buffer.push(mkChunk({ timeMs: 0, isKeyFrame: true, stats }));
        for (let i = 1; i <= 12; i++) // span well past 300, but only one keyframe
            buffer.push(mkChunk({ timeMs: 33 * i, isKeyFrame: false, stats }));

        expect(buffer.count()).toBe(13);
        expect(stats.dropTrace.get(FrameDropStage.ReceiverSkipToLive)).toBeUndefined();
    });

    it('respects the cooldown between skips', () => {
        const stats = createEmptyPlayerStats();
        let nowMs = 0;
        const buffer = new EncodedFrameBuffer({
            targetSpanMs: 100,
            frameDurationMs: 33.333,
            stats,
            skipToLiveSpanMs: 300,
            nowFn: () => nowMs,
        });

        buffer.push(mkChunk({ timeMs: 0, isKeyFrame: true, stats }));
        for (let i = 1; i <= 8; i++)
            buffer.push(mkChunk({ timeMs: 33 * i, isKeyFrame: false, stats }));
        buffer.push(mkChunk({ timeMs: 297, isKeyFrame: true, stats })); // first skip fires
        expect(stats.dropTrace.get(FrameDropStage.ReceiverSkipToLive)).toBe(9);

        // Within cooldown (1500 ms): build another stale backlog + newer keyframe.
        nowMs = 1000;
        for (let i = 1; i <= 8; i++)
            buffer.push(mkChunk({ timeMs: 297 + 33 * i, isKeyFrame: false, stats }));
        buffer.push(mkChunk({ timeMs: 600, isKeyFrame: true, stats }));
        // Still 9 — cooldown blocked the second skip.
        expect(stats.dropTrace.get(FrameDropStage.ReceiverSkipToLive)).toBe(9);
    });
});

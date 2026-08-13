import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

type FastRaf = typeof import('fast-raf');

let rafQueue: FrameRequestCallback[] = [];
let fastRaf: FastRaf;

// The module holds its buckets at module scope, so every test needs a fresh copy.
async function load(): Promise<void> {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'performance'] });
    rafQueue = [];
    globalThis.requestAnimationFrame = (callback: FrameRequestCallback) => rafQueue.push(callback);
    vi.resetModules();
    fastRaf = await import('fast-raf');
}

// Runs the timers up to `time`, then hands that instant to whatever asked for a frame. The two
// extra microtask yields let the scheduler's own await - the one separating the phases - resume.
async function advanceTo(time: number): Promise<void> {
    vi.advanceTimersByTime(time - performance.now());
    for (let i = 0; i < 4 && rafQueue.length > 0; i++) {
        const callbacks = rafQueue;
        rafQueue = [];
        for (const callback of callbacks)
            callback(time);
        await Promise.resolve();
        await Promise.resolve();
    }
}

describe('fastRaf', () => {
    beforeEach(load);
    afterEach(() => vi.useRealTimers());

    it('should run reads before writes', async () => {
        const order = new Array<string>();
        fastRaf.fastRaf({ read: () => order.push('read'), write: () => order.push('write') });

        await advanceTo(16);
        expect(order).toEqual(['read', 'write']);
    });

    it('should run every read before any write when two frequencies fall due together', async () => {
        // At 60ms the next 100ms instant and the next 50ms one are both 100ms, so the 10Hz and
        // 20Hz buckets share a frame - the case that made a render-batch flush dirty the DOM
        // under the streaming-height loop's measurements.
        vi.advanceTimersByTime(60);
        const order = new Array<string>();
        fastRaf.fastRaf10({ read: () => order.push('read10'), write: () => order.push('write10') });
        fastRaf.fastRaf20({ read: () => order.push('read20'), write: () => order.push('write20') });

        await advanceTo(100);
        expect(order.slice(0, 2).sort()).toEqual(['read10', 'read20']);
        expect(order.slice(2).sort()).toEqual(['write10', 'write20']);
    });

    it('should resume an awaited fastReadRafAsync before the writes of that frame', async () => {
        const order = new Array<string>();
        void fastRaf.fastReadRafAsync().then(() => order.push('awaitedRead'));
        fastRaf.fastRaf({ write: () => order.push('write') });

        await advanceTo(16);
        expect(order).toEqual(['awaitedRead', 'write']);
    });

    it('should hold a frequency-limited callback until its instant', async () => {
        const order = new Array<string>();
        fastRaf.fastRaf10({ write: () => order.push('write10') });

        await advanceTo(64);
        expect(order).toEqual([]);

        await advanceTo(104);
        expect(order).toEqual(['write10']);
    });

    it('should reject a duplicate key within the same frame', async () => {
        const order = new Array<string>();
        expect(fastRaf.fastRaf({ key: 'k', write: () => order.push('first') })).toBe(true);
        expect(fastRaf.fastRaf({ key: 'k', write: () => order.push('second') })).toBe(false);

        await advanceTo(16);
        expect(order).toEqual(['first']);
    });
});

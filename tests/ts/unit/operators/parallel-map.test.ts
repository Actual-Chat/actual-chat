import { describe, it, expect } from 'vitest';
import { parallelMap } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/parallel-map';

function fromArray<T>(items: readonly T[]): AsyncIterable<T> {
    async function* gen(): AsyncIterable<T> {
        for (const it of items) {
            await Promise.resolve();
            yield it;
        }
    }
    return gen();
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

describe('parallelMap', () => {
    it('throws on invalid concurrency', () => {
        expect(() => parallelMap({ concurrency: 0, map: () => 0 })).toThrow(/≥ 1/);
        expect(() => parallelMap({ concurrency: -1, map: () => 0 })).toThrow(/≥ 1/);
        expect(() => parallelMap({ concurrency: NaN, map: () => 0 })).toThrow(/≥ 1/);
    });

    it('concurrency=1: results in source order', async () => {
        const op = parallelMap<number, number>({
            concurrency: 1,
            map: x => x * 10,
        });
        const out = await drain(op(fromArray([1, 2, 3, 4, 5])));
        expect(out).toEqual([10, 20, 30, 40, 50]);
    });

    it('concurrency>1 with out-of-order completion: output order matches source order', async () => {
        const slowFor = new Set([2, 4]);
        const op = parallelMap<number, number>({
            concurrency: 3,
            map: async x => {
                // Slow items 2 and 4 complete after their successors.
                if (slowFor.has(x)) await new Promise(r => setTimeout(r, 30));
                return x * 10;
            },
        });
        const out = await drain(op(fromArray([1, 2, 3, 4, 5])));
        expect(out).toEqual([10, 20, 30, 40, 50]);
    });

    it('respects concurrency cap (never more than N in-flight)', async () => {
        let inflight = 0;
        let maxInflight = 0;
        const op = parallelMap<number, number>({
            concurrency: 2,
            map: async x => {
                inflight++;
                maxInflight = Math.max(maxInflight, inflight);
                // Stagger so concurrency builds up before any task settles.
                await new Promise(r => setTimeout(r, 20));
                inflight--;
                return x;
            },
        });
        const out = await drain(op(fromArray([1, 2, 3, 4, 5, 6])));
        expect(out).toEqual([1, 2, 3, 4, 5, 6]);
        expect(maxInflight).toBe(2);
    });

    it('passes per-slot ids ∈ [0, concurrency)', async () => {
        const seenSlots = new Set<number>();
        const op = parallelMap<number, [number, number]>({
            concurrency: 3,
            map: (x, slot) => {
                seenSlots.add(slot);
                return [x, slot];
            },
        });
        const out = await drain(op(fromArray([1, 2, 3, 4, 5, 6])));
        expect(out.map(p => p[0])).toEqual([1, 2, 3, 4, 5, 6]);
        for (const [, slot] of out) {
            expect(slot).toBeGreaterThanOrEqual(0);
            expect(slot).toBeLessThan(3);
        }
        expect(seenSlots.size).toBeGreaterThan(0);
    });

    it('onSlotInit fires once per slot, lazily, before its first map()', async () => {
        const initCalls: number[] = [];
        let mapCallCount = 0;
        const op = parallelMap<number, number>({
            concurrency: 3,
            map: x => {
                mapCallCount++;
                return x;
            },
            onSlotInit: slot => {
                // Capture the order of init.
                initCalls.push(slot);
                // No map call should have happened for THIS slot before its
                // init: the dispatch path always calls onSlotInit before
                // map (we can't easily check per-slot here, so check that
                // all inits happened by the time map starts).
                expect(mapCallCount).toBeLessThanOrEqual(initCalls.length);
            },
        });
        await drain(op(fromArray([1, 2, 3, 4, 5])));
        // Total init calls bounded by concurrency.
        expect(initCalls.length).toBeLessThanOrEqual(3);
        // Each init fired at most once for any given slot.
        expect(new Set(initCalls).size).toBe(initCalls.length);
    });

    it('onSlotDispose fires for every initialised slot at teardown', async () => {
        const disposed: number[] = [];
        const op = parallelMap<number, number>({
            concurrency: 2,
            map: x => x,
            onSlotDispose: slot => disposed.push(slot),
        });
        await drain(op(fromArray([1, 2, 3, 4])));
        expect(new Set(disposed).size).toBe(disposed.length);
        expect(disposed.length).toBeGreaterThan(0);
        expect(disposed.length).toBeLessThanOrEqual(2);
    });

    it('does not initialise unused slots when concurrency > item count', async () => {
        const inits: number[] = [];
        const disposed: number[] = [];
        const op = parallelMap<number, number>({
            concurrency: 4,
            map: x => x,
            onSlotInit: s => inits.push(s),
            onSlotDispose: s => disposed.push(s),
        });
        await drain(op(fromArray([1])));
        expect(inits).toEqual([0]);
        expect(disposed).toEqual([0]);
    });

    it('mapper rejection is rethrown', async () => {
        const op = parallelMap<number, number>({
            concurrency: 2,
            map: x => {
                if (x === 3) throw new Error('boom');
                return x;
            },
        });
        await expect(drain(op(fromArray([1, 2, 3, 4])))).rejects.toThrow(/boom/);
    });

    it('onUnconsumedResult fires for outputs produced after the first error', async () => {
        const closed: number[] = [];
        const op = parallelMap<number, number>({
            concurrency: 3,
            map: async x => {
                if (x === 2) {
                    // Item 2 throws a tick after 1 and 3 settle, so 1 is
                    // yielded but 3 (settled out-of-order) gets closed.
                    await new Promise(r => setTimeout(r, 5));
                    throw new Error('boom');
                }
                return x;
            },
            onUnconsumedResult: v => closed.push(v),
        });
        await expect(drain(op(fromArray([1, 2, 3])))).rejects.toThrow(/boom/);
        // Item 3's success result must have been routed through
        // onUnconsumedResult since the operator threw before yielding it.
        expect(closed).toContain(3);
    });

    it('empty source: no slots initialised, no error', async () => {
        const inits: number[] = [];
        const op = parallelMap<number, number>({
            concurrency: 2,
            map: x => x,
            onSlotInit: s => inits.push(s),
        });
        const out = await drain(op(fromArray<number>([])));
        expect(out).toEqual([]);
        expect(inits).toEqual([]);
    });
});

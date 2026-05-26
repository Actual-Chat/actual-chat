import { describe, it, expect } from 'vitest';
import { resilientStream, expRetryDelays } from 'resilient-stream';

async function collect<T>(iterable: AsyncIterable<T>): Promise<T[]> {
    const result: T[] = [];
    for await (const item of iterable)
        result.push(item);
    return result;
}

function fromArray<T>(items: T[]): AsyncIterable<T> {
    return (async function* () {
        for (const item of items)
            yield await Promise.resolve(item);
    })();
}

function failAfter<T>(items: T[], error: Error): AsyncIterable<T> {
    return (async function* () {
        for (const item of items)
            yield await Promise.resolve(item);
        throw error;
    })();
}

describe('resilientStream', () => {
    it('should complete normally', async () => {
        const stream = resilientStream<number>({
            provider: () => fromArray([1, 2, 3, 4, 5]),
        });

        const result = await collect(stream);
        expect(result).toEqual([1, 2, 3, 4, 5]);
    });

    it('should handle empty source', async () => {
        const stream = resilientStream<number>({
            provider: () => fromArray([]),
        });

        const result = await collect(stream);
        expect(result).toEqual([]);
    });

    it('should retry on transient error', async () => {
        let attempt = 0;
        const stream = resilientStream<number>({
            provider: () => {
                attempt++;
                if (attempt === 1)
                    return failAfter([1, 2], new Error('transient'));
                return fromArray([3, 4, 5]);
            },
            retryDelays: () => 0,
        });

        const result = await collect(stream);
        expect(result).toEqual([1, 2, 3, 4, 5]);
        expect(attempt).toBe(2);
    });

    it('should emit reset item on retry', async () => {
        let attempt = 0;
        const stream = resilientStream<number>({
            provider: () => {
                attempt++;
                if (attempt === 1)
                    return failAfter([1, 2], new Error('transient'));
                return fromArray([3, 4]);
            },
            resetItem: -1,
            retryDelays: () => 0,
        });

        const result = await collect(stream);
        expect(result).toEqual([1, 2, -1, 3, 4]);
    });

    it('should throw when max retries exhausted', async () => {
        let attempt = 0;
        const stream = resilientStream<number>({
            provider: () => {
                attempt++;
                return failAfter([], new Error(`fail ${attempt}`));
            },
            maxRetries: 3,
            retryDelays: () => 0,
        });

        await expect(collect(stream)).rejects.toThrow('fail 3');
        expect(attempt).toBe(3);
    });

    it('should retry when provider itself throws', async () => {
        let attempt = 0;
        const stream = resilientStream<number>({
            provider: () => {
                attempt++;
                if (attempt <= 2)
                    throw new Error('factory failure');
                return fromArray([1, 2]);
            },
            retryDelays: () => 0,
        });

        const result = await collect(stream);
        expect(result).toEqual([1, 2]);
        expect(attempt).toBe(3);
    });

    it('should cancel via abort signal', async () => {
        const abortController = new AbortController();
        const stream = resilientStream<number>({
            provider: () => slowStream(abortController),
            signal: abortController.signal,
        });

        const result: number[] = [];
        await expect(async () => {
            for await (const item of stream)
                result.push(item);
        }).rejects.toThrow();
        expect(result).toContain(1);
    });

    it('should use exponential retry delays', () => {
        const delays = expRetryDelays(100, 5000);
        expect(delays(0)).toBe(100);
        expect(delays(1)).toBe(200);
        expect(delays(2)).toBe(400);
        expect(delays(3)).toBe(800);
        expect(delays(10)).toBe(5000); // capped
    });

    it('should support multiple iterations', async () => {
        let callCount = 0;
        const stream = resilientStream<number>({
            provider: () => {
                callCount++;
                return fromArray([1, 2, 3]);
            },
        });

        const result1 = await collect(stream);
        const result2 = await collect(stream);
        expect(result1).toEqual([1, 2, 3]);
        expect(result2).toEqual([1, 2, 3]);
        expect(callCount).toBe(2);
    });
});

async function* slowStream(abortController: AbortController): AsyncIterable<number> {
    yield 1;
    await new Promise(resolve => setTimeout(resolve, 50));
    abortController.abort();
    await new Promise(resolve => setTimeout(resolve, 5000));
    yield 2;
}

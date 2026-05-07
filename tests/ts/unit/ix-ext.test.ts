import { describe, it, expect } from 'vitest';
import { drain } from '../../../src/nodejs/src/ix-ext';

async function* emptySource(): AsyncIterable<unknown> {
    // no items
}

describe('drain', () => {
    it('runs an async iterable to completion', async () => {
        await expect(drain(emptySource())).resolves.toBeUndefined();
    });

    it('silences errors accepted by the optional predicate', async () => {
        const expected = new Error('expected shutdown');
        async function* source(): AsyncIterable<unknown> {
            await Promise.resolve();
            throw expected;
        }

        await expect(drain(source(), e => e === expected)).resolves.toBeUndefined();
    });

    it('propagates errors rejected by the optional predicate', async () => {
        async function* source(): AsyncIterable<unknown> {
            await Promise.resolve();
            throw new Error('boom');
        }

        await expect(drain(source(), () => false)).rejects.toThrow('boom');
    });
});

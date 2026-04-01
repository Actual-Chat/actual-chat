import { describe, it, expect } from 'vitest';
import {
    PromiseSource,
    OperationCancelledError,
    cancelled,
    type Cancelled,
    waitAsync,
    delayAsync,
} from 'promises';

describe('waitAsync cancellation', () => {
    it('should throw OperationCancelledError when cancel resolves', async () => {
        const cancelSource = new PromiseSource<Cancelled>();
        const promise = waitAsync(delayAsync(1000), cancelSource);
        cancelSource.resolve(cancelled);

        await expect(promise).rejects.toThrow(OperationCancelledError);
    });

    it('should resolve normally when not cancelled', async () => {
        const promise = waitAsync(delayAsync(10));
        await expect(promise).resolves.toBeUndefined();
    });
});

describe('PromiseSource', () => {
    it('should resolve with value', async () => {
        const source = new PromiseSource<number>();
        source.resolve(42);
        await expect(source).resolves.toBe(42);
    });

    it('should reject with error', async () => {
        const source = new PromiseSource<number>();
        source.reject(new Error('test error'));
        await expect(source).rejects.toThrow('test error');
    });

    it('should ignore duplicate resolve', async () => {
        const source = new PromiseSource<number>();
        source.resolve(1);
        source.resolve(2); // should be ignored
        await expect(source).resolves.toBe(1);
    });

    it('should report isCompleted correctly', () => {
        const source = new PromiseSource<void>();
        expect(source.isCompleted()).toBe(false);
        source.resolve(undefined);
        expect(source.isCompleted()).toBe(true);
    });
});

describe('delayAsync', () => {
    it('should resolve after delay', async () => {
        const start = Date.now();
        await delayAsync(50);
        const elapsed = Date.now() - start;
        expect(elapsed).toBeGreaterThanOrEqual(40); // allow some timer jitter
    });
});

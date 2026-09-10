import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiReconnectDelayer } from '../../../src/nodejs/src/api/api-reconnect-delayer';

// The delayer is what a worker's RPC peer waits on between reconnect attempts.
// `isRequired` may park it for good; `isOnline` is a cross-realm copy of the
// main thread's state and must only slow it down.

function createDelayer(): ApiReconnectDelayer {
    const delayer = new ApiReconnectDelayer();
    delayer.offlineProbeDelayMs = 15_000;
    return delayer;
}

// A delay resolves through a short promise chain, so drain the microtask queue before looking.
async function settled(promise: Promise<void>): Promise<'resolved' | 'rejected' | 'pending'> {
    let outcome: 'resolved' | 'rejected' | 'pending' = 'pending';
    void promise.then(() => { outcome = 'resolved'; }, () => { outcome = 'rejected'; });
    for (let i = 0; i < 10; i++)
        await Promise.resolve();

    return outcome;
}

describe('ApiReconnectDelayer', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('parks while no scope requires a connection and wakes when one does', async () => {
        const delayer = createDelayer();
        const delay = delayer.getDelay(0);
        vi.advanceTimersByTime(600_000);
        expect(await settled(delay.promise)).toBe('pending');

        delayer.setIsRequired(true);
        expect(await settled(delay.promise)).toBe('resolved');
    });

    it('stays parked through an online flip when still not required', async () => {
        const delayer = createDelayer();
        const delay = delayer.getDelay(0);
        delayer.setIsOnline(false);
        delayer.setIsOnline(true);
        await Promise.resolve();
        expect(await settled(delay.promise)).toBe('pending');

        delayer.setIsRequired(true);
        expect(await settled(delay.promise)).toBe('resolved');
    });

    it('rejects a parked delay when the peer stops', async () => {
        const delayer = createDelayer();
        const stop = new AbortController();
        const delay = delayer.getDelay(0, stop.signal);
        stop.abort();
        expect(await settled(delay.promise)).toBe('rejected');
    });

    it('uses the base backoff while required and online', async () => {
        const delayer = createDelayer();
        delayer.setIsRequired(true);
        expect(delayer.getDelay(0).endsAt).toBe(0);

        const delay = delayer.getDelay(1);
        expect(delay.endsAt - Date.now()).toBeLessThan(15_000);
        vi.advanceTimersByTime(delay.endsAt - Date.now());
        expect(await settled(delay.promise)).toBe('resolved');
    });

    it('stretches retries to the probe delay while offline instead of parking', async () => {
        const delayer = createDelayer();
        delayer.setIsRequired(true);
        delayer.setIsOnline(false);
        const delay = delayer.getDelay(1);
        expect(delay.endsAt - Date.now()).toBe(15_000);

        vi.advanceTimersByTime(14_999);
        expect(await settled(delay.promise)).toBe('pending');
        vi.advanceTimersByTime(1);
        expect(await settled(delay.promise)).toBe('resolved');
    });

    it('keeps the first attempt immediate even while offline', () => {
        const delayer = createDelayer();
        delayer.setIsRequired(true);
        delayer.setIsOnline(false);
        expect(delayer.getDelay(0).endsAt).toBe(0);
    });

    it('wakes an offline probe delay as soon as the hint flips online', async () => {
        const delayer = createDelayer();
        delayer.setIsRequired(true);
        delayer.setIsOnline(false);
        const delay = delayer.getDelay(1);
        vi.advanceTimersByTime(1000);
        expect(await settled(delay.promise)).toBe('pending');

        delayer.setIsOnline(true);
        expect(await settled(delay.promise)).toBe('resolved');
    });

    it('does not wake a running backoff on a flip to offline', async () => {
        const delayer = createDelayer();
        delayer.setIsRequired(true);
        const delay = delayer.getDelay(3);
        delayer.setIsOnline(false);
        await Promise.resolve();
        expect(await settled(delay.promise)).toBe('pending');
    });
});

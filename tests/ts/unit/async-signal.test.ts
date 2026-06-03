import { describe, it, expect } from 'vitest';
import { AsyncSignal } from 'actuallab-core';

// Flush the microtask queue so pending .then() callbacks run.
async function flush(): Promise<void> {
    for (let i = 0; i < 8; i++) await Promise.resolve();
}

function track(p: Promise<void>): { resolved: () => boolean } {
    let done = false;
    void p.then(() => { done = true; });
    return { resolved: () => done };
}

describe('AsyncSignal', () => {
    it('resolves a waiter parked before the notify', async () => {
        const s = new AsyncSignal();
        const w = s.wait();
        const t = track(w);

        await flush();
        expect(t.resolved()).toBe(false); // no notify yet

        s.notify();
        await w;
        expect(t.resolved()).toBe(true);
    });

    it('is edge-triggered: a notify with no waiter is not latched', async () => {
        const s = new AsyncSignal();
        s.notify(); // no one waiting — must NOT latch

        const t = track(s.wait());
        await flush();
        expect(t.resolved()).toBe(false); // the prior notify is gone

        s.notify();
        await flush();
        expect(t.resolved()).toBe(true);
    });

    it('releases every waiter parked at the moment of notify', async () => {
        const s = new AsyncSignal();
        const w1 = s.wait();
        const w2 = s.wait();
        const t1 = track(w1);
        const t2 = track(w2);

        s.notify();
        await Promise.all([w1, w2]);
        expect(t1.resolved()).toBe(true);
        expect(t2.resolved()).toBe(true);
    });

    it('a waiter taken after a notify waits for the next notify', async () => {
        const s = new AsyncSignal();
        const w1 = s.wait();
        s.notify();
        await w1;

        const t2 = track(s.wait());
        await flush();
        expect(t2.resolved()).toBe(false); // fresh promise, not the consumed one

        s.notify();
        await flush();
        expect(t2.resolved()).toBe(true);
    });

    it('lost-wakeup-safe: notify between wait() and await still wins', async () => {
        const s = new AsyncSignal();
        // Consumer takes the wait promise, then a producer notifies before the
        // consumer reaches its await — the held promise must still resolve.
        const w = s.wait();
        s.notify();
        await expect(Promise.race([w, Promise.reject(new Error('would hang'))]))
            .resolves.toBeUndefined();
    });
});

import { describe, it, expect } from 'vitest';
import { ServerClock } from 'server-clock';

describe('ServerClock', () => {
    it('should default to zero offset', () => {
        expect(ServerClock.offsetMs).toBe(0);
    });

    it('should return now close to Date.now when offset is zero', () => {
        const before = Date.now();
        const serverNow = ServerClock.now();
        const after = Date.now();
        expect(serverNow).toBeGreaterThanOrEqual(before);
        expect(serverNow).toBeLessThanOrEqual(after);
    });

    it('should update offset from server time', () => {
        const serverTime = Date.now() + 5000; // server is 5s ahead
        ServerClock.updateOffset(serverTime);
        expect(Math.abs(ServerClock.offsetMs - 5000)).toBeLessThan(50); // allow jitter

        // now() should reflect the offset
        const serverNow = ServerClock.now();
        expect(Math.abs(serverNow - serverTime)).toBeLessThan(50);

        // Reset offset for other tests
        ServerClock.updateOffset(Date.now());
    });

    it('should notify listeners on offset change', () => {
        const offsets: number[] = [];
        const unsubscribe = ServerClock.onOffsetChanged(offset => offsets.push(offset));

        ServerClock.updateOffset(Date.now() + 1000);
        ServerClock.updateOffset(Date.now() + 2000);

        expect(offsets.length).toBe(2);
        expect(Math.abs(offsets[0] - 1000)).toBeLessThan(50);
        expect(Math.abs(offsets[1] - 2000)).toBeLessThan(50);

        unsubscribe();

        // After unsubscribe, no more notifications
        ServerClock.updateOffset(Date.now());
        expect(offsets.length).toBe(2);
    });

    it('should support multiple listeners', () => {
        let count1 = 0, count2 = 0;
        const unsub1 = ServerClock.onOffsetChanged(() => count1++);
        const unsub2 = ServerClock.onOffsetChanged(() => count2++);

        ServerClock.updateOffset(Date.now() + 100);
        expect(count1).toBe(1);
        expect(count2).toBe(1);

        unsub1();
        ServerClock.updateOffset(Date.now());
        expect(count1).toBe(1); // no longer listening
        expect(count2).toBe(2);

        unsub2();
    });
});

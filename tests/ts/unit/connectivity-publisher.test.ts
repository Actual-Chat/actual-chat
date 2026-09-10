import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ConnectivityUI } from '../../../src/dotnet/UI.Blazor/Services/ConnectivityUI/connectivity-ui';

type Push = [isOnline: boolean, isConnected: boolean, isBlazorServer: boolean];

describe('ConnectivityUI.publishTo', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        ConnectivityUI.init(null, false);
        ConnectivityUI.setOnline(true);
        ConnectivityUI.setConnected(true);
    });
    afterEach(() => vi.useRealTimers());

    it('pushes once ready, on every change, and periodically in between', async () => {
        const pushes: Push[] = [];
        const publisher = ConnectivityUI.publishTo((...args) => { pushes.push(args); }, 1000);
        await Promise.resolve();
        expect(pushes).toEqual([[true, true, false]]);

        ConnectivityUI.setConnected(false);
        expect(pushes).toHaveLength(2);
        expect(pushes[1]).toEqual([true, false, false]);

        vi.advanceTimersByTime(2000);
        expect(pushes).toHaveLength(4);
        expect(pushes[3]).toEqual([true, false, false]);

        publisher.dispose();
        ConnectivityUI.setConnected(true);
        vi.advanceTimersByTime(5000);
        expect(pushes).toHaveLength(4);
    });

    it('publishes on demand and survives a rejecting target', async () => {
        let calls = 0;
        const publisher = ConnectivityUI.publishTo(() => {
            calls++;
            return Promise.reject(new Error('worker gone'));
        }, 1000);
        await Promise.resolve();
        publisher.publish();
        expect(calls).toBe(2);

        vi.advanceTimersByTime(1000);
        expect(calls).toBe(3);
        publisher.dispose();
    });
});

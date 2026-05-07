import { describe, it, expect, afterEach } from 'vitest';
import { ServerClock } from 'clocks';
import { SharedSettings } from 'shared-settings';
import { SharedSettingsWorkerSync, type SharedSettingsWorker } from 'shared-settings-worker';

afterEach(() => {
    SharedSettings.update({ serverClockOffsetMs: 0, apiUrl: undefined });
});

describe('SharedSettings', () => {
    it('updates local server clock and notifies via EventHandlerSet', () => {
        const snapshots: number[] = [];
        const handler = SharedSettings.changed.add(settings => snapshots.push(settings.serverClockOffsetMs));

        SharedSettings.update({ serverClockOffsetMs: 1234 });

        expect(ServerClock.offsetMs).toBeGreaterThan(1200);
        expect(ServerClock.offsetMs).toBeLessThan(1270);
        expect(snapshots).toEqual([1234]);

        handler.dispose();
    });

    it('registers workers with an IDisposable and pushes current/future settings', () => {
        const received: number[] = [];
        const target: SharedSettingsWorker = {
            updateSharedSettings: (settings) => {
                received.push(settings.serverClockOffsetMs);
                return Promise.resolve();
            },
        };

        SharedSettings.update({ serverClockOffsetMs: 10 });
        const registration = SharedSettingsWorkerSync.register(target);
        SharedSettings.update({ serverClockOffsetMs: 20 });
        registration.dispose();
        SharedSettings.update({ serverClockOffsetMs: 30 });

        expect(received).toEqual([10, 20]);
    });
});

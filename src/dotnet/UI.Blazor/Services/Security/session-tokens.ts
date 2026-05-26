import { PromiseSource } from 'actuallab-core';
import { getLogs } from 'logging';
import { SharedSettings } from 'shared-settings';

const { debugLog } = getLogs('SessionTokens');

const DefaultMinLifespanMs = 10_000;

export class SessionTokens {
    public static readonly headerName = 'Session';

    private static token = '';
    private static expiresAtMs = 0;
    private static whenChanged = new PromiseSource<void>();

    public static async get(minLifespanMs = DefaultMinLifespanMs): Promise<string> {
        for (;;) {
            const whenChanged = this.whenChanged;
            if (this.expiresAtMs - Date.now() >= minLifespanMs)
                return this.token;

            await whenChanged;
        }
    }

    public static set(token: string, expiresAtMs: number): void {
        debugLog?.log('set');
        this.setCore(token, expiresAtMs);
    }

    // Private methods

    private static setCore(token: string, expiresAtMs: number): void {
        this.token = token;
        this.expiresAtMs = expiresAtMs;

        this.whenChanged.resolve(undefined);
        this.whenChanged = new PromiseSource<void>();

        // Push into SharedSettings so workers (which can't reach
        // `SessionTokens` directly across the worker boundary) get the
        // fresh token via the existing SharedSettingsWorkerSync bridge.
        SharedSettings.update({ sessionToken: token });
    }
}

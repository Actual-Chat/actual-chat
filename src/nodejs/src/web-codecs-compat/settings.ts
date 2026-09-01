// Main-thread half: reads the debug override from localStorage, resolves it to
// a level, and publishes it through SharedSettings so every worker realm installs
// the same one. Separate from init.ts because localStorage exists only here, and
// because SharedSettings imports init.ts.

import { getLogs } from 'logging';
import { SharedSettings } from 'shared-settings';
import { isWebCodecsLevel, WebCodecsCompat, type WebCodecsLevelOverride } from './init';

const { infoLog } = getLogs('WebCodecsCompat');

const OVERRIDE_KEY = 'video.debug.webCodecsLevel';
const BASE_URL = '/dist/libav';

export function getWebCodecsLevelOverride(): WebCodecsLevelOverride {
    try {
        const raw = globalThis.localStorage.getItem(OVERRIDE_KEY);

        return isWebCodecsLevel(raw) ? raw : 'auto';
    } catch {
        return 'auto';
    }
}

// Applies on the next load: the codec ladder and every worker are built from the
// level in force when they started.
export function setWebCodecsLevelOverride(override: WebCodecsLevelOverride | null): void {
    try {
        if (override === null || override === 'auto') {
            globalThis.localStorage.removeItem(OVERRIDE_KEY);
            return;
        }

        if (!isWebCodecsLevel(override))
            throw new Error(`Unknown WebCodecs level: ${String(override)}`);

        globalThis.localStorage.setItem(OVERRIDE_KEY, override);
    } catch (error) {
        infoLog?.log('setWebCodecsLevelOverride failed:', error);
    }
}

export function initWebCodecsCompat(): void {
    const override = getWebCodecsLevelOverride();
    const level = WebCodecsCompat.resolveLevel(override);
    infoLog?.log(`initWebCodecsCompat: override='${override}' -> level='${level}'`);
    SharedSettings.update({ webCodecs: { level, baseUrl: BASE_URL } });
}

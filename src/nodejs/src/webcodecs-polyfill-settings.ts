// Main-thread half of the WebCodecs polyfill: reads the debug override from
// localStorage, resolves it to a level, and publishes it through SharedSettings
// so every worker realm applies the same one. Split from webcodecs-polyfill.ts
// because localStorage exists only here.

import { getLogs } from 'logging';
import { SharedSettings } from 'shared-settings';
import {
    isWebCodecsPolyfillLevel,
    resolveWebCodecsPolyfillLevel,
    type WebCodecsPolyfillOverride,
} from 'webcodecs-polyfill';

const { infoLog } = getLogs('WebCodecsPolyfill');

const OVERRIDE_KEY = 'video.debug.webCodecsPolyfillLevel';
const BASE_URL = '/dist/libav';

export function getWebCodecsPolyfillOverride(): WebCodecsPolyfillOverride {
    try {
        const raw = globalThis.localStorage.getItem(OVERRIDE_KEY);

        return isWebCodecsPolyfillLevel(raw) ? raw : 'auto';
    } catch {
        return 'auto';
    }
}

// Takes effect on the next page load: the polyfill cannot be unloaded, and the
// codec ladder and every worker are built from the level that was in force when
// they started.
export function setWebCodecsPolyfillOverride(override: WebCodecsPolyfillOverride | null): void {
    try {
        if (override === null || override === 'auto') {
            globalThis.localStorage.removeItem(OVERRIDE_KEY);
            return;
        }
        if (!isWebCodecsPolyfillLevel(override))
            throw new Error(`Unknown WebCodecs polyfill level: ${String(override)}`);

        globalThis.localStorage.setItem(OVERRIDE_KEY, override);
    } catch (error) {
        infoLog?.log('setWebCodecsPolyfillOverride failed:', error);
    }
}

export function initWebCodecsPolyfill(): void {
    const override = getWebCodecsPolyfillOverride();
    const level = resolveWebCodecsPolyfillLevel(override);
    infoLog?.log(`initWebCodecsPolyfill: override='${override}' -> level='${level}'`);
    SharedSettings.update({ webCodecsPolyfill: { level, baseUrl: BASE_URL } });
}

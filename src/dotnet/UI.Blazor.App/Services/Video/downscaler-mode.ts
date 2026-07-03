// Diagnostics-selectable sender downscaler backend, backed by localStorage so
// the choice survives reloads. Read by the recorder when it builds the worker
// config; applies on the next pipeline restart (the recorder already restarts on
// ladder/config changes). Default 'metadata' (HW encoder rescales from the
// ceiling-coded frame) — it keeps the GPU off the downscale path; 'webgl'/'canvas'
// stay selectable as the override fallback. See operators/downscale.ts.

import type { DownscalerMode } from './operators/downscale';

const KEY = 'video.debug.downscalerMode';
const DEFAULT_MODE: DownscalerMode = 'metadata';
const MODES: readonly DownscalerMode[] = ['webgl', 'canvas', 'metadata', 'webgpu'];

function isMode(value: string | null): value is DownscalerMode {
    return value !== null && (MODES as readonly string[]).includes(value);
}

export function getDownscalerMode(): DownscalerMode {
    try {
        const raw = globalThis.localStorage.getItem(KEY);
        return isMode(raw) ? raw : DEFAULT_MODE;
    } catch {
        return DEFAULT_MODE;
    }
}

export function setDownscalerMode(mode: DownscalerMode): void {
    try {
        if (mode === DEFAULT_MODE) {
            globalThis.localStorage.removeItem(KEY);
            return;
        }
        globalThis.localStorage.setItem(KEY, mode);
    } catch {
        // Debug-only setting; ignore storage failures in private/sandboxed contexts.
    }
}

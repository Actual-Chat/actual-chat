// Diagnostics-selectable sender downscaler backend, backed by localStorage so
// the choice survives reloads. Read by the recorder when it builds the worker
// config; applies on the next pipeline restart (the recorder already restarts on
// ladder/config changes). Default 'webgl-nv12' (WebGL2 RGB→NV12 at tier size via
// synchronous readPixels, so the HW encoder skips its internal libyuv
// ConvertAndScale + readback). WebGL readback does NOT contend the HW encoder on
// mobile, unlike WebGPU mapAsync (382 ms spikes → <30 fps); trace 211233 vs the
// WebGPU paths delivered the most frames at the lowest GPU-proc. Self-falls-back
// to 'metadata' when WebGL2 is unavailable. 'webgpu'/'webgl'/'canvas'/'metadata'
// stay selectable. See operators/downscale.ts + webgl/nv12-downscaler.ts.

import type { DownscalerMode } from './operators/downscale';

const KEY = 'video.debug.downscalerMode';
const DEFAULT_MODE: DownscalerMode = 'webgl-nv12';
const MODES: readonly DownscalerMode[] = ['webgl', 'canvas', 'metadata', 'webgpu', 'webgpu-2pass', 'webgl-nv12'];

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

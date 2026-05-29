import { DeviceInfo } from 'device-info';
import type { RenderBackendKind } from '../../Services/Video/playback/render-backends';

export type RenderBackendOverride = RenderBackendKind | null;

// Shared policy for remote playback and local preview. Firefox uses canvas by
// default (no MSTG/VTG). iOS is also gated to canvas: Safari's `<video
// srcObject>` of a synthetic VTG track pauses itself after the first frame on
// remote playback, and the playback watchdog underruns because `readyState` /
// `currentTime` reads stay stale post-pause. `?renderBackend=mstg` still
// forces the generator path for diagnostics, and `?renderBackend=canvas`
// forces canvas everywhere.
export function isMstgRenderBackendPlausible(): boolean {
    return !DeviceInfo.isFirefox && !DeviceInfo.isIos;
}

export function readRenderBackendOverride(href = getCurrentHref()): RenderBackendOverride {
    try {
        const flag = new URL(href).searchParams.get('renderBackend');
        return flag === 'canvas' || flag === 'mstg' ? flag : null;
    } catch {
        return null;
    }
}

function getCurrentHref(): string {
    try {
        return globalThis.location.href;
    } catch {
        return '';
    }
}

export function pickRenderBackendKind(
    override: RenderBackendOverride = readRenderBackendOverride(),
    mstgPlausible = isMstgRenderBackendPlausible(),
): RenderBackendKind {
    if (override)
        return override;
    return mstgPlausible ? 'mstg' : 'canvas';
}

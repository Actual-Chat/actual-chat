import { DeviceInfo } from 'device-info';
import type { RenderBackendKind } from '../../Services/Video/playback/render-backends';

export type RenderBackendOverride = RenderBackendKind | null;

// Shared policy for remote playback and local preview. Edge and Firefox use
// canvas by default; `?renderBackend=mstg` still forces the generator path for
// diagnostics, and `?renderBackend=canvas` forces canvas everywhere.
export function isMstgRenderBackendPlausible(): boolean {
    return !DeviceInfo.isFirefox && !DeviceInfo.isEdge;
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

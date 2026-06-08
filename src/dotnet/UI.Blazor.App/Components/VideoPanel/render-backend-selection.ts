import type { RenderBackendKind } from '../../Services/Video/playback/render-backends';
import { DeviceInfo } from 'device-info';
import { getCodecCategory } from '../../Services/Video/codec-support';

export type RenderBackendOverride = RenderBackendKind | null;

// Backend policy for remote playback and local preview. Every browser defaults
// to the generator path ('mstg' — a main-thread MediaStreamTrackGenerator on
// Chromium, or a worker-side VideoTrackGenerator on Safari/Firefox). A browser
// that exposes no generator is demoted to canvas at runtime by the
// worker→main→canvas fallback chain (see video-player `startWorkerForAttempt`),
// and a <video> whose playback stalls is demoted by the playback watchdog. The
// only static control is the URL override: `?renderBackend=canvas` forces
// canvas everywhere, `?renderBackend=mstg` forces the generator path.
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

// Desktop Edge playing HEVC is negative-gated to canvas: Chromium-on-Windows
// mis-composites a rotated `<video srcObject>` at focused-tile sizes (the
// hardware-overlay path) — the whole focused tile paints black while the
// bg-blur canvas, which draws the VideoFrame through a 2D context, stays live,
// and the small thumbnail of the same stream renders fine. Canvas backend
// avoids the overlay path entirely. The gate is narrowed to desktop Edge +
// HEVC (the only confirmed-broken combination, and HEVC is what rotated iPhone
// senders use); other browsers, mobile Edge, and non-HEVC streams keep MSTG.
// `codec` is omitted for local preview, which is never affected.
// `?renderBackend=mstg` opts back in for diagnostics.
export function pickRenderBackendKind(
    override: RenderBackendOverride = readRenderBackendOverride(),
    codec?: string,
): RenderBackendKind {
    if (override)
        return override;
    if (codec && DeviceInfo.isEdge && !DeviceInfo.isMobile && getCodecCategory(codec.toLowerCase()) === 'hevc')
        return 'canvas';
    return 'mstg';
}

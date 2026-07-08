// Diagnostics-selectable camera capture fps, backed by localStorage so the
// choice survives reloads. null = automatic (the recorder's capture-fps
// follower paces the camera from viewer demand); a number pins the capture
// renegotiation target regardless of demand — used to A/B the camera
// ISP/stabilization pipeline cost without staging real viewer topologies.

const KEY = 'video.debug.captureFpsOverride';
const ALLOWED_FPS = [5, 10, 15, 24, 30];

export function getCaptureFpsOverride(): number | null {
    try {
        const raw = globalThis.localStorage.getItem(KEY);
        if (raw === null)
            return null;
        const fps = Number(raw);
        return ALLOWED_FPS.includes(fps) ? fps : null;
    } catch {
        return null;
    }
}

export function setCaptureFpsOverride(fps: number | null): void {
    try {
        if (fps === null || !ALLOWED_FPS.includes(fps)) {
            globalThis.localStorage.removeItem(KEY);
            return;
        }
        globalThis.localStorage.setItem(KEY, String(fps));
    } catch {
        // Debug-only setting; ignore storage failures in private/sandboxed contexts.
    }
}

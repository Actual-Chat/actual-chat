// Diagnostics-selectable video SENDER backend, backed by localStorage so the
// choice survives reloads. Read by the recorder when it starts; applies on the
// next recording start (switch, then re-join the call). Default 'webcodecs'
// (the production multi-encoder simulcast path). 'webrtc' routes to the
// experimental WebRTC encoder-tap backend (see sender/webrtc/). See
// downscaler-mode.ts for the mirrored pattern.

import type { SenderBackendKind } from './sender/sender-backend';

const KEY = 'video.debug.senderBackend';
const DEFAULT_MODE: SenderBackendKind = 'webcodecs';
const MODES: readonly SenderBackendKind[] = ['webcodecs', 'webrtc'];

function isMode(value: string | null): value is SenderBackendKind {
    return value !== null && (MODES as readonly string[]).includes(value);
}

export function getSenderBackendMode(): SenderBackendKind {
    try {
        const raw = globalThis.localStorage.getItem(KEY);
        return isMode(raw) ? raw : DEFAULT_MODE;
    } catch {
        return DEFAULT_MODE;
    }
}

export function setSenderBackendMode(mode: SenderBackendKind): void {
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

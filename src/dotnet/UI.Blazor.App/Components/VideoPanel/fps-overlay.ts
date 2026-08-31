import { getCodecCategory } from '../../Services/Video/codec-support';
import { getActiveRecorder } from './video-recorder';
import { getActivePlayers } from './video-player';

const STORAGE_KEY = 'video.debug.showFpsOverlay';
const CODEC_STORAGE_KEY = 'video.debug.showCodecOverlay';
// U+2E31 WORD SEPARATOR MIDDLE DOT — already wide enough to need no spaces.
const CODEC_SEPARATOR = '\u2E31';
const BODY_CLASS = 'show-fps-overlay';
const POLL_INTERVAL_MS = 1000;

let pollTimer: number | null = null;

export function getShowFpsOverlay(): boolean {
    try {
        return globalThis.localStorage.getItem(STORAGE_KEY) === '1';
    } catch {
        return false;
    }
}

export function setShowFpsOverlay(enabled: boolean): void {
    try {
        if (enabled)
            globalThis.localStorage.setItem(STORAGE_KEY, '1');
        else
            globalThis.localStorage.removeItem(STORAGE_KEY);
    } catch {
        // Debug-only setting; ignore storage failures in private/sandboxed contexts.
    }
    applyBodyClass(enabled);
    if (enabled) ensurePollerRunning();
    else stopPoller();
}

export function getShowCodecOverlay(): boolean {
    try {
        return globalThis.localStorage.getItem(CODEC_STORAGE_KEY) === '1';
    } catch {
        return false;
    }
}

// Rides on the FPS overlay's element and body class, so it shows nothing on
// its own — the FPS overlay has to be on too.
export function setShowCodecOverlay(enabled: boolean): void {
    try {
        if (enabled)
            globalThis.localStorage.setItem(CODEC_STORAGE_KEY, '1');
        else
            globalThis.localStorage.removeItem(CODEC_STORAGE_KEY);
    } catch {
        // Debug-only setting; ignore storage failures in private/sandboxed contexts.
    }
}

export function initFpsOverlay(): void {
    const enabled = getShowFpsOverlay();
    applyBodyClass(enabled);
    if (enabled) ensurePollerRunning();
}

function applyBodyClass(enabled: boolean): void {
    const body = globalThis.document.body;
    if (enabled) body.classList.add(BODY_CLASS);
    else body.classList.remove(BODY_CLASS);
}

function ensurePollerRunning(): void {
    if (pollTimer !== null) return;
    pollTimer = globalThis.setInterval(tick, POLL_INTERVAL_MS) as unknown as number;
}

function stopPoller(): void {
    if (pollTimer === null) return;
    globalThis.clearInterval(pollTimer);
    pollTimer = null;
}

function tick(): void {
    const elements = globalThis.document.querySelectorAll<HTMLElement>('.video-fps');
    if (elements.length === 0) return;
    const withCodec = getShowCodecOverlay();
    for (const el of elements) {
        const streamId = el.dataset.streamId;
        const sourceKindRaw = el.dataset.sourceKind;
        const isReceiver = !!streamId;
        const rate = isReceiver
            ? getReceiverRate(streamId)
            : sourceKindRaw !== undefined
                ? getSenderRate(Number(sourceKindRaw))
                : null;
        if (rate === null) {
            el.textContent = '';
            continue;
        }

        const codec = withCodec
            ? (isReceiver ? getReceiverCodec(streamId) : getSenderCodec(Number(sourceKindRaw)))
            : null;
        const rateText = String(Math.round(rate));
        el.textContent = codec ? `${codec}${CODEC_SEPARATOR}${rateText}` : rateText;
    }
}

function getSenderRate(kind: number): number | null {
    return getActiveRecorder(kind)?.peekBundlesPerSec() ?? null;
}

function getReceiverRate(streamId: string): number | null {
    return getActivePlayers().get(streamId)?.peekPresentedPerSec() ?? null;
}

function getSenderCodec(kind: number): string | null {
    return toCodecLabel(getActiveRecorder(kind)?.peekCodec());
}

function getReceiverCodec(streamId: string): string | null {
    return toCodecLabel(getActivePlayers().get(streamId)?.peekCodec());
}

// The category, not the full profile string: 'avc1.640028' is far too long to
// sit in a corner of a video tile.
function toCodecLabel(codec: string | null | undefined): string | null {
    if (!codec) return null;
    return getCodecCategory(codec).toUpperCase();
}

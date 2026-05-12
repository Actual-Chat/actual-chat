import { getActiveRecorder } from './video-recorder';
import { getActivePlayers } from './video-player';

const STORAGE_KEY = 'video.debug.showFpsOverlay';
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
    for (const el of elements) {
        const streamId = el.dataset.streamId;
        const sourceKindRaw = el.dataset.sourceKind;
        const rate = streamId
            ? getReceiverRate(streamId)
            : sourceKindRaw !== undefined
                ? getSenderRate(Number(sourceKindRaw))
                : null;
        el.textContent = rate !== null ? String(Math.round(rate)) : '';
    }
}

function getSenderRate(kind: number): number | null {
    return getActiveRecorder(kind)?.peekBundlesPerSec() ?? null;
}

function getReceiverRate(streamId: string): number | null {
    return getActivePlayers().get(streamId)?.peekPresentedPerSec() ?? null;
}

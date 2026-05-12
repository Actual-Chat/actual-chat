import { getActiveRecorder } from './video-recorder';
import { getActivePlayers } from './video-player';

const STORAGE_KEY = 'video.debug.showFpsOverlay';
const BODY_CLASS = 'show-fps-overlay';
const POLL_INTERVAL_MS = 1000;

interface OverlayState {
    lastPrimary: number;
    lastTimestampMs: number;
}
// Per-element rate tracker keyed by the DOM node itself so removed tiles
// drop out of the map automatically when their elements are GC'd via the
// WeakMap. The cumulative counter and the moment it was sampled are the
// only two values needed to derive an FPS reading on the next tick.
const overlayState = new WeakMap<Element, OverlayState>();
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
    const elements = globalThis.document.querySelectorAll<HTMLElement>('.video-fps-overlay');
    if (elements.length === 0) return;
    const nowMs = performance.now();
    for (const el of elements) {
        const streamId = el.dataset.streamId;
        const sourceKindRaw = el.dataset.sourceKind;
        const primary = streamId
            ? getReceiverPrimary(streamId)
            : sourceKindRaw !== undefined
                ? getSenderPrimary(Number(sourceKindRaw))
                : null;
        updateElement(el, primary, nowMs);
    }
}

function getSenderPrimary(kind: number): number | null {
    const recorder = getActiveRecorder(kind);
    if (!recorder) return null;
    const diag = recorder.getDiagnostics();
    return diag.encodedFrames;
}

function getReceiverPrimary(streamId: string): number | null {
    // Avoid getDiagnosticsAsync()'s worker round-trip on the hot path: the
    // player's renderFrameCount is bumped synchronously from latency-tap
    // reports and is close enough for a 1Hz overlay.
    const player = getActivePlayers().get(streamId);
    if (!player) return null;
    return player.peekPresentedCount();
}

function updateElement(el: HTMLElement, primary: number | null, nowMs: number): void {
    if (primary === null) {
        el.textContent = '—';
        overlayState.delete(el);
        return;
    }
    const prev = overlayState.get(el);
    overlayState.set(el, { lastPrimary: primary, lastTimestampMs: nowMs });
    if (!prev) {
        el.textContent = '…';
        return;
    }
    const dtMs = nowMs - prev.lastTimestampMs;
    if (dtMs <= 0) return;
    const fps = Math.max(0, primary - prev.lastPrimary) * 1000 / dtMs;
    el.textContent = `${fps.toFixed(0)} fps`;
}

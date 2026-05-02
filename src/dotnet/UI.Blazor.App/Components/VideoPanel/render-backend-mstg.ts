import { getLogs } from 'logging';
import type { PresentableFrame, RenderBackend } from './render-backend';

const { debugLog, infoLog, warnLog } = getLogs('VideoPlayer');

// Off-thread support: the generator may live in main (Chromium MSTG) and/or
// worker (Safari VTG, Chromium MSTG). We can't probe worker globals from main,
// so we try off-thread on browsers where SOMETHING is plausible — Safari and
// any context exposing MediaStreamTrackGenerator on main — and let the worker
// throw on failure. Caller falls back to canvas on rejection.
export function isOffThreadPlausible(): boolean {
    if (typeof globalThis === 'undefined') return false;
    const g = globalThis as { MediaStreamTrackGenerator?: unknown };
    if (typeof g.MediaStreamTrackGenerator === 'function') return true;
    // Safari probably has VideoTrackGenerator only inside workers.
    const ua = typeof navigator !== 'undefined' ? navigator.userAgent : '';
    if (/^((?!chrome|android).)*safari/i.test(ua)) return true;
    return false;
}

// Off-thread renderer: just an HTMLVideoElement adapter. The decoder worker
// owns the generator + writable + selector + Fusion RPC pull (see
// `startPullInWorker` in decoder-worker-contract.ts). When the worker emits
// `onOffThreadTrackReady`, video-player calls `onTrackReady` here and we
// attach the MediaStreamTrack to <video srcObject>. The bg backdrop is
// handled separately via OffscreenCanvas transferred to the worker (§13);
// this backend doesn't see the bg canvas.
export class OffThreadRenderBackend implements RenderBackend {
    readonly kind = 'mstg' as const;
    readonly isOffThread = true;
    private trackAttached = false;
    private disposed = false;
    // Cached aspect-ratio applied to the parent container — same role as in
    // CanvasRenderBackend. Skip redundant DOM writes when the live videoEl
    // dims still resolve to the same ratio.
    private lastAspectRatio = '';
    private resizeListener: (() => void) | null = null;
    // DIAG: watchdog state — captures whether <video> playback is actually
    // advancing. The blur backdrop is painted by the worker from the same
    // decoded frames, so if mainWrites/s is healthy yet currentTime stays
    // frozen, the bug is in <video srcObject> playback, not the pipeline.
    private watchdogTimer: ReturnType<typeof setInterval> | null = null;
    private lastWatchdogCurrentTime = 0;
    private lastWatchdogAtMs = 0;
    // F1: track consecutive stall ticks before re-calling play().
    private consecutiveStallTicks = 0;
    // F2: observe parent classList flips (item-x ↔ item-focused) — the
    // user-confirmed trigger of a born-at-sidebar player going black.
    // Promotion to focused doesn't auto-resume <video srcObject> playback,
    // so we kick play() on every classList change.
    private parentClassObserver: MutationObserver | null = null;
    private lastObservedParentCls = '';
    // Tracked focused state (parent has `item-focused`). Used to fire
    // onFocusedChange only on edge transitions.
    private lastObservedFocused: boolean | null = null;
    /**
     * Optional hook invoked whenever the parent's `item-focused` class flips.
     * VideoPlayer uses this to disable the worker-side blur paint for sidebar
     * tiles (CSS hides the bg canvas there anyway). Set externally after ctor.
     */
    onFocusedChange: ((focused: boolean) => void) | null = null;

    constructor(private readonly videoEl: HTMLVideoElement) {}

    getOutputSize(): { width: number; height: number } | null {
        const width = this.videoEl.videoWidth;
        const height = this.videoEl.videoHeight;
        return width > 0 && height > 0 ? { width, height } : null;
    }

    onTrackReady(track: MediaStreamTrack): void {
        if (this.disposed) {
            warnLog?.log(`onTrackReady: backend disposed, dropping track id=${track.id} readyState=${track.readyState}`);
            try { track.stop(); } catch { /* ignore */ }
            return;
        }
        if (this.trackAttached) {
            const existingStream = this.videoEl.srcObject;
            const existingTracks = existingStream instanceof MediaStream ? existingStream.getTracks() : [];
            const existingIds = existingTracks.map(t => `${t.id}:${t.readyState}`).join(',');
            warnLog?.log(
                `onTrackReady: called twice; ignoring second track. ` +
                `newTrack=${track.id}:${track.readyState}, existingTracks=[${existingIds}], ` +
                `videoPaused=${this.videoEl.paused}, videoReadyState=${this.videoEl.readyState}, ` +
                `videoCurrentTime=${this.videoEl.currentTime.toFixed(2)}s, ` +
                `videoWidth=${this.videoEl.videoWidth}x${this.videoEl.videoHeight}`);
            try { track.stop(); } catch { /* ignore */ }
            return;
        }
        infoLog?.log(
            `onTrackReady: first attach, track=${track.id}:${track.readyState}, ` +
            `srcObjectWasNull=${this.videoEl.srcObject === null}`);
        this.trackAttached = true;
        const stream = new MediaStream([track]);
        this.videoEl.srcObject = stream;
        // DIAG: start the playback watchdog now that a track is attached.
        this.startWatchdog();
        // `resize` fires whenever videoWidth/videoHeight change — including the
        // initial attach (after `loadedmetadata`) and every mid-stream rotation
        // that flips the encoder dims (no Format republish, no Blazor
        // re-render — the receiver's only signal that source dims changed).
        this.resizeListener = () => this.applyContainerAspect();
        this.videoEl.addEventListener('resize', this.resizeListener);
        // First attach may already have dims if the metadata event landed
        // before the listener was registered; flush once eagerly.
        this.applyContainerAspect();
        this.tryPlay('initial');
        this.startParentClassObserver();
        infoLog?.log('Off-thread track attached to <video srcObject>');
    }

    // F1+F2 helper: re-call play() and clear the stall counter so we don't
    // call again immediately. Logs why it fired so production traces show
    // recovery activity. Track is still live across these calls; this just
    // nudges the <video> element to actually paint.
    private tryPlay(reason: string): void {
        if (this.disposed) return;
        this.consecutiveStallTicks = 0;
        this.videoEl.play().catch((e: unknown) =>
            warnLog?.log(`video.play() rejected (${reason}):`, e));
    }

    drawFrame(_pf: PresentableFrame): void {
        // No-op: off-thread path. Main thread never feeds frames here.
    }

    // Mirrors CanvasRenderBackend.applyContainerAspect: writes the source
    // aspect ratio to the parent `.video-track-player` container so the CSS
    // `.has-source-aspect.item-focused[style*="aspect-ratio"]` rule fits the
    // tile to the source instead of the panel.
    private applyContainerAspect(): void {
        const w = this.videoEl.videoWidth;
        const h = this.videoEl.videoHeight;
        if (w <= 0 || h <= 0) return;
        const ratio = `${w} / ${h}`;
        if (ratio === this.lastAspectRatio) return;
        const parent = this.videoEl.parentElement;
        if (!parent) return;
        parent.style.aspectRatio = ratio;
        this.lastAspectRatio = ratio;
        debugLog?.log(`Container aspect-ratio set to ${ratio} (mstg)`);
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.stopWatchdog();
        this.stopParentClassObserver();
        if (this.resizeListener) {
            try { this.videoEl.removeEventListener('resize', this.resizeListener); } catch { /* ignore */ }
            this.resizeListener = null;
        }
        try {
            const stream = this.videoEl.srcObject;
            if (stream instanceof MediaStream) {
                for (const t of stream.getTracks()) t.stop();
            }
        } catch { /* ignore */ }
        try { this.videoEl.srcObject = null; } catch { /* ignore */ }
    }

    // F2: when Blazor re-renders the parent's class attribute (e.g., layout
    // flips item-x ↔ item-focused), Chromium can leave the <video> element
    // paused after the brief layout transition even though the MSTG track
    // is still feeding frames. Kick play() on every classList change.
    // (Visibility itself is owned by inline `style.display` on the elements,
    // set in video-player.ts applyBackendVisibility — so layout flips no
    // longer hide the video; this observer only nudges playback.)
    private startParentClassObserver(): void {
        if (this.parentClassObserver !== null) return;
        const parent = this.videoEl.parentElement;
        if (!parent) return;
        this.lastObservedParentCls = parent.className;
        this.lastObservedFocused = parent.classList.contains('item-focused');
        // Initial fire so consumers can sync state at startup (the player may
        // be born already focused or not).
        if (this.onFocusedChange) this.onFocusedChange(this.lastObservedFocused);
        this.parentClassObserver = new MutationObserver(() => {
            if (this.disposed) return;
            const cls = parent.className;
            if (cls === this.lastObservedParentCls) return;
            this.lastObservedParentCls = cls;
            warnLog?.log(`startParentClassObserver: parent classList changed → "${cls}", retrying play()`);
            this.tryPlay('parent-classlist-change');
            const focused = parent.classList.contains('item-focused');
            if (focused !== this.lastObservedFocused) {
                this.lastObservedFocused = focused;
                if (this.onFocusedChange) this.onFocusedChange(focused);
            }
        });
        this.parentClassObserver.observe(parent, { attributes: true, attributeFilter: ['class'] });
    }

    private stopParentClassObserver(): void {
        if (this.parentClassObserver === null) return;
        try { this.parentClassObserver.disconnect(); } catch { /* ignore */ }
        this.parentClassObserver = null;
    }

    // DIAG: every 2s, capture whether currentTime is advancing and what the
    // computed visual state of the <video> looks like. When the user reports
    // "blur visible, no main video", these logs distinguish between:
    //   - currentTime frozen   → playback stalled (track not feeding, or paused)
    //   - currentTime advances → playback fine, so bug is layout/CSS/visibility
    private startWatchdog(): void {
        if (this.watchdogTimer !== null) return;
        this.lastWatchdogCurrentTime = this.videoEl.currentTime;
        this.lastWatchdogAtMs = performance.now();
        this.watchdogTimer = setInterval(() => this.tickWatchdog(), 2000);
    }

    private stopWatchdog(): void {
        if (this.watchdogTimer === null) return;
        clearInterval(this.watchdogTimer);
        this.watchdogTimer = null;
    }

    private tickWatchdog(): void {
        if (this.disposed) return;
        const nowMs = performance.now();
        const nowCt = this.videoEl.currentTime;
        const dtMs = nowMs - this.lastWatchdogAtMs;
        const dCt = nowCt - this.lastWatchdogCurrentTime;
        this.lastWatchdogAtMs = nowMs;
        this.lastWatchdogCurrentTime = nowCt;
        const stream = this.videoEl.srcObject;
        const tracks = stream instanceof MediaStream ? stream.getTracks() : [];
        const trackInfo = tracks.map(t => `${t.kind}:${t.readyState}:muted=${t.muted}:enabled=${t.enabled}`).join('|');
        let cssInfo = '';
        try {
            const cs = globalThis.getComputedStyle(this.videoEl);
            cssInfo = ` cssDisplay=${cs.display} opacity=${cs.opacity} zIndex=${cs.zIndex} visibility=${cs.visibility}`;
        } catch { /* ignore */ }
        const cls = this.videoEl.parentElement?.className ?? '';
        // F1: detect playback stall and self-heal. dCt should grow ~dt/1000s
        // each tick when the <video> is rendering. Threshold of 50 ms over
        // a 2 s window is a generous buffer for slow ticks; a true stall
        // sits at 0. Fire after two consecutive stalls so a single dropped
        // tick (e.g. main-thread jank) doesn't trigger spurious play()
        // calls in already-healthy state.
        const isStalled = this.videoEl.paused || dCt < 0.05;
        if (isStalled) {
            this.consecutiveStallTicks++;
            if (this.consecutiveStallTicks >= 2 && tracks.length > 0) {
                warnLog?.log(
                    `tickWatchdog: stall detected (paused=${this.videoEl.paused}, ` +
                    `dCt=${dCt.toFixed(3)}s, ticks=${this.consecutiveStallTicks}), retrying play()`);
                this.tryPlay('watchdog-stall');
            }
        } else {
            this.consecutiveStallTicks = 0;
        }
        infoLog?.log(
            `tickWatchdog: paused=${this.videoEl.paused} ` +
            `currentTime=${nowCt.toFixed(3)}s dCt=${dCt.toFixed(3)}s dt=${dtMs.toFixed(0)}ms ` +
            `videoWH=${this.videoEl.videoWidth}x${this.videoEl.videoHeight} ` +
            `readyState=${this.videoEl.readyState} networkState=${this.videoEl.networkState} ` +
            `tracks=[${trackInfo}]${cssInfo} parentCls="${cls}"`);
    }
}

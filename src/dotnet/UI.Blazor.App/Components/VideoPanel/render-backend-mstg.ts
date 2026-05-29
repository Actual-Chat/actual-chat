import { getLogs } from 'logging';
import type { PresentableFrame, RenderBackend } from './render-backend';
import { applyRotationLayout } from '../../Services/Video/services/tile-fit';
import { MstgPlaybackWatchdog, type MstgPlaybackStallReport } from './mstg-playback-watchdog';
import { isMstgRenderBackendPlausible } from './render-backend-selection';

const { debugLog, infoLog, warnLog } = getLogs('VideoPlayer');

export type OffThreadPlaybackStallReport = MstgPlaybackStallReport;

// Off-thread support: only Firefox lacks MSTG/VTG. Every other browser we
// target (Chromium, Safari) exposes a generator either in main (MSTG) or
// inside a worker (VTG). The canvas backend is broken on Chromium/Safari, so
// negative-gate on Firefox rather than positive-probe APIs that may be hidden
// behind worker globals we can't reach from main.
export function isOffThreadPlausible(): boolean {
    return isMstgRenderBackendPlausible();
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
    private rotationQuarter = 0;
    private currentFit: 'cover' | 'contain' = 'cover';
    private expectedPaused = false;
    private resizeListener: (() => void) | null = null;
    // DIAG: watchdog watches whether <video> playback is actually advancing
    // and falls back to the canvas backend on a true stall. See
    // mstg-playback-watchdog.ts for details and the kill switch.
    private readonly watchdog: MstgPlaybackWatchdog;
    // F2: observe parent classList flips (item-x ↔ item-focused) — the
    // user-confirmed trigger of a born-at-sidebar player going black.
    // Promotion to focused doesn't auto-resume <video srcObject> playback,
    // so we kick play() on every classList change.
    private parentClassObserver: MutationObserver | null = null;
    // Tracked focused state (parent has `item-focused`). Used as the gate for
    // both tryPlay() and onFocusedChange — observer fires on any class
    // mutation but we only act on actual focus flips.
    private lastObservedFocused: boolean | null = null;
    /**
     * Optional hook invoked whenever the parent's `item-focused` class flips.
     * VideoPlayer uses this to disable the worker-side blur paint for sidebar
     * tiles (CSS hides the bg canvas there anyway). Set externally after ctor.
     */
    onFocusedChange: ((focused: boolean) => void) | null = null;
    onPlaybackStalled: ((report: OffThreadPlaybackStallReport) => void) | null = null;

    constructor(private readonly videoEl: HTMLVideoElement) {
        this.watchdog = new MstgPlaybackWatchdog({
            videoEl,
            tryPlay: (reason) => this.tryPlay(reason),
            onStall: (report) => this.onPlaybackStalled?.(report),
            isPaused: () => this.expectedPaused,
        });
    }

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
            // Replacement: each VideoPlayer restart constructs a fresh MSTG track.
            const existingStream = this.videoEl.srcObject;
            if (existingStream instanceof MediaStream) {
                for (const t of existingStream.getTracks()) {
                    try { t.stop(); } catch { /* ignore */ }
                }
            }
            try { this.videoEl.srcObject = new MediaStream([track]); } catch { /* ignore */ }
            this.watchdog.resetCounters();
            this.tryPlay('track-replaced');
            infoLog?.log(`onTrackReady: replaced track, new=${track.id}:${track.readyState}`);
            return;
        }
        infoLog?.log(
            `onTrackReady: first attach, track=${track.id}:${track.readyState}, ` +
            `srcObjectWasNull=${this.videoEl.srcObject === null}`);
        this.trackAttached = true;
        const stream = new MediaStream([track]);
        this.videoEl.srcObject = stream;
        // DIAG: start the playback watchdog now that a track is attached.
        this.watchdog.start();
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

    // F1+F2 helper: re-call play(). Logs why it fired so production traces
    // show recovery activity. Track is still live across these calls; this
    // just nudges the <video> element to actually paint.
    private tryPlay(reason: string): void {
        if (this.disposed) return;
        this.videoEl.play().catch((e: unknown) =>
            warnLog?.log(`video.play() rejected (${reason}):`, e));
    }

    drawFrame(_pf: PresentableFrame): void {
        // No-op: off-thread path. Main thread never feeds frames here.
    }

    setRotation(quarter: number): void {
        const q = ((Math.round(quarter) % 4) + 4) % 4;
        if (q === this.rotationQuarter) return;
        this.rotationQuarter = q;
        applyRotationLayout(this.videoEl, q);
        this.applyContainerAspect(true);
    }

    recomputeLayout(): void {
        if ((this.rotationQuarter & 1) === 1)
            applyRotationLayout(this.videoEl, this.rotationQuarter);
    }

    setFit(fit: 'cover' | 'contain'): void {
        if (fit === this.currentFit) return;
        this.currentFit = fit;
        this.videoEl.style.objectFit = fit;
    }

    // No-op: bg blur runs in the player worker (see VideoPlayer.applyBackdrop).
    // The interface argument is preserved so callers don't need to branch on
    // backend kind, but the canvas reference is ignored.
    setBackdrop(_canvas: HTMLCanvasElement | null, _focused: boolean): void {
        // intentionally empty
    }

    setExpectedPaused(paused: boolean): void {
        if (paused === this.expectedPaused) return;
        this.expectedPaused = paused;
        // Resuming clears stall counters so the first post-resume tick starts
        // from zero — there's a natural gap while the server's keyframe-lock
        // re-yields its first frame.
        if (!paused)
            this.watchdog.resetCounters();
    }

    // Mirrors CanvasRenderBackend.applyContainerAspect: writes the source
    // aspect ratio to the parent `.video-track-player` container so the CSS
    // `.has-source-aspect.item-focused[style*="aspect-ratio"]` rule fits the
    // tile to the source instead of the panel.
    private applyContainerAspect(force = false): void {
        const w = this.videoEl.videoWidth;
        const h = this.videoEl.videoHeight;
        if (w <= 0 || h <= 0) return;
        // 90/270 turns swap visual W/H, so parent's aspect-ratio must invert.
        const swap = (this.rotationQuarter & 1) === 1;
        const visibleW = swap ? h : w;
        const visibleH = swap ? w : h;
        const ratio = `${visibleW} / ${visibleH}`;
        if (!force && ratio === this.lastAspectRatio) return;
        const parent = this.videoEl.parentElement;
        if (!parent) return;
        parent.style.aspectRatio = ratio;
        this.lastAspectRatio = ratio;
        debugLog?.log(`Container aspect-ratio set to ${ratio} (mstg rotation=${this.rotationQuarter})`);
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.watchdog.dispose();
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

    // F2: when Blazor re-renders the parent's class attribute and item-focused
    // flips (item-x ↔ item-focused), Chromium can leave the <video> element
    // paused after the brief layout transition even though the MSTG track is
    // still feeding frames. Kick play() on focus flips. The observer fires on
    // any class mutation but we early-exit when item-focused didn't change —
    // sidebar-position renumbering (item-x item-1 ↔ item-x item-0) and other
    // class flips don't pause the video.
    // (Visibility itself is owned by inline `style.display` on the elements,
    // set in video-player.ts applyBackendVisibility — so layout flips no
    // longer hide the video; this observer only nudges playback.)
    private startParentClassObserver(): void {
        if (this.parentClassObserver !== null) return;
        const parent = this.videoEl.parentElement;
        if (!parent) return;
        this.lastObservedFocused = parent.classList.contains('item-focused');
        // Initial fire so consumers can sync state at startup (the player may
        // be born already focused or not).
        if (this.onFocusedChange) this.onFocusedChange(this.lastObservedFocused);
        this.parentClassObserver = new MutationObserver(() => {
            if (this.disposed) return;
            const focused = parent.classList.contains('item-focused');
            if (focused === this.lastObservedFocused) return;
            this.lastObservedFocused = focused;
            debugLog?.log(`startParentClassObserver: focused → ${focused}, retrying play()`);
            this.tryPlay('parent-classlist-change');
            if (this.onFocusedChange) this.onFocusedChange(focused);
        });
        this.parentClassObserver.observe(parent, { attributes: true, attributeFilter: ['class'] });
    }

    private stopParentClassObserver(): void {
        if (this.parentClassObserver === null) return;
        try { this.parentClassObserver.disconnect(); } catch { /* ignore */ }
        this.parentClassObserver = null;
    }

}

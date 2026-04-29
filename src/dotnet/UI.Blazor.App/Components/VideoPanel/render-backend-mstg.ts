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

    constructor(private readonly videoEl: HTMLVideoElement) {}

    onTrackReady(track: MediaStreamTrack): void {
        if (this.disposed) {
            try { track.stop(); } catch { /* ignore */ }
            return;
        }
        if (this.trackAttached) {
            warnLog?.log('onTrackReady called twice; ignoring second track');
            try { track.stop(); } catch { /* ignore */ }
            return;
        }
        this.trackAttached = true;
        const stream = new MediaStream([track]);
        this.videoEl.srcObject = stream;
        // `resize` fires whenever videoWidth/videoHeight change — including the
        // initial attach (after `loadedmetadata`) and every mid-stream rotation
        // that flips the encoder dims (no Format republish, no Blazor
        // re-render — the receiver's only signal that source dims changed).
        this.resizeListener = () => this.applyContainerAspect();
        this.videoEl.addEventListener('resize', this.resizeListener);
        // First attach may already have dims if the metadata event landed
        // before the listener was registered; flush once eagerly.
        this.applyContainerAspect();
        this.videoEl.play().catch((e: unknown) => warnLog?.log('video.play() rejected:', e));
        infoLog?.log('Off-thread track attached to <video srcObject>');
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
}

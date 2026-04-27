import { getLogs } from 'logging';
import type { PresentableFrame, RenderBackend } from './render-backend';

const { infoLog, warnLog } = getLogs('VideoPlayer');

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
// attach the MediaStreamTrack to <video srcObject>.
export class OffThreadRenderBackend implements RenderBackend {
    readonly kind = 'mstg' as const;
    readonly isOffThread = true;
    private trackAttached = false;
    private disposed = false;

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
        this.videoEl.srcObject = new MediaStream([track]);
        this.videoEl.play().catch((e: unknown) => warnLog?.log('video.play() rejected:', e));
        infoLog?.log('Off-thread track attached to <video srcObject>');
    }

    drawFrame(_pf: PresentableFrame): void {
        // No-op: off-thread path. Main thread never feeds frames here.
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        try {
            const stream = this.videoEl.srcObject;
            if (stream instanceof MediaStream) {
                for (const t of stream.getTracks()) t.stop();
            }
        } catch { /* ignore */ }
        try { this.videoEl.srcObject = null; } catch { /* ignore */ }
    }
}

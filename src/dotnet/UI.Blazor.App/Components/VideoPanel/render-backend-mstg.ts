import { getLogs } from 'logging';
import { AudioVideoSync } from 'audio-video-sync';
import type { DecoderWorker } from '../../Services/Video/workers/decoder-worker-contract';
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

// Off-thread renderer: the worker constructs the generator (MSTG on Chromium,
// VTG on Safari), keeps the writable, runs audio-clock selection, and ships
// the resulting MediaStreamTrack back via the onOffThreadTrackReady callback.
// Main attaches the track to <video srcObject> — the browser paints, and
// VideoPlayer's main-thread render loop is silent on this backend.
export class OffThreadRenderBackend implements RenderBackend {
    readonly kind = 'mstg' as const;
    readonly isOffThread = true;
    private syncChannel: MessageChannel | null = null;
    private subscribedAuthorId: string | null = null;
    private trackAttached = false;
    private disposed = false;

    constructor(private readonly videoEl: HTMLVideoElement) {}

    // Fired by the rpc callback wired in VideoPlayer when the worker's
    // generator produces its track. Attach to <video srcObject> and play.
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

    // Asks the worker to construct the generator + run selection. Returns true
    // on success, false if the worker has no usable API — caller swaps to
    // canvas backend.
    async setupWorker(
        decoderWorker: DecoderWorker,
        authorId: string,
        startedAtMs: number,
        jitterBufferMs: number,
    ): Promise<boolean> {
        if (this.disposed) return false;
        const channel = new MessageChannel();
        this.syncChannel = channel;
        this.subscribedAuthorId = authorId;
        AudioVideoSync.subscribeWorker(authorId, channel.port1);
        try {
            await decoderWorker.enableOffThreadRenderer(startedAtMs, jitterBufferMs, channel.port2);
            return true;
        } catch (e) {
            warnLog?.log('Off-thread renderer rejected by worker, falling back to canvas:', e);
            this.cleanupSyncChannel();
            return false;
        }
    }

    drawFrame(_pf: PresentableFrame): void {
        // No-op: off-thread path. Main thread never feeds frames here.
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.cleanupSyncChannel();
        try {
            const stream = this.videoEl.srcObject;
            if (stream instanceof MediaStream) {
                for (const t of stream.getTracks()) t.stop();
            }
        } catch { /* ignore */ }
        try { this.videoEl.srcObject = null; } catch { /* ignore */ }
    }

    private cleanupSyncChannel(): void {
        if (this.syncChannel && this.subscribedAuthorId) {
            AudioVideoSync.unsubscribeWorker(this.subscribedAuthorId, this.syncChannel.port1);
            try { this.syncChannel.port1.close(); } catch { /* ignore */ }
            this.syncChannel = null;
            this.subscribedAuthorId = null;
        }
    }
}

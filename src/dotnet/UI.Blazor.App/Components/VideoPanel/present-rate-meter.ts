// Diagnostics-only union present-rate meter. Watches every live-stream
// <video> element (remote tiles + self-preview) via requestVideoFrameCallback
// and reports how many distinct display slots per second the union of them
// touches — the leading indicator for full-screen WebView redraw rate on
// mobile, where every invalidation redraws the whole functor.

const WINDOW_MS = 2000;
// Same-display-frame tolerance; bins expectedDisplayTime (vsync-aligned),
// not presentationTime (submission time — scatters sub-vsync).
const SLOT_BIN_MS = 4;
// collect() keeps the meter alive; no calls for this long → self-stop.
const IDLE_STOP_MS = 3000;

export interface PresentRateSnapshot {
    presentsPerSec: number;
    slotsPerSec: number;
    videoCount: number;
    // Unobservable (canvas backend / no rVFC) tiles: if this is nonzero, a 0 rate means "unknown", not "stalled".
    unobservableCount: number;
    // Per-stream display rate (distinct display slots/sec) for <video> elements
    // carrying data-stream-id — the honest "what the eye sees" number, unlike
    // the per-stream throughput counter which is the MSTG write rate.
    slotsPerSecByStream: Record<string, number>;
}

// rVFC fires for a subset of presented frames — measured at ~4.6 callbacks/s
// against 20.3 frames actually presented — so counting callbacks understates the
// rate several-fold. `presentedFrames` is the element's own count of every frame
// it put on screen; it resets when the track is swapped (tier change).
interface PresentSample {
    displayAt: number;
    presentedFrames: number;
}

/** Frames the element actually put on screen per second, spanning the samples we
 *  have. Falls back to the callback count when the counter reset mid-window. */
function ratePerSec(samples: PresentSample[], windowSec: number): number {
    if (samples.length < 2)
        return samples.length / windowSec;

    const first = samples[0];
    const last = samples[samples.length - 1];
    const spanSec = (last.displayAt - first.displayAt) / 1000;
    const presented = last.presentedFrames - first.presentedFrames;
    if (spanSec <= 0 || presented < 0)
        return samples.length / windowSec;

    return presented / spanSec;
}

class PresentRateMeter {
    private readonly watched = new Map<HTMLVideoElement, number>();
    private readonly presentsByVideo = new Map<HTMLVideoElement, PresentSample[]>();
    private rescanTimer: ReturnType<typeof setInterval> | null = null;
    private lastCollectAt = 0;
    private unobservableCount = 0;

    collect(): PresentRateSnapshot {
        this.lastCollectAt = performance.now();
        if (this.rescanTimer === null) {
            this.rescan();
            this.rescanTimer = setInterval(() => this.onRescanTick(), 1000);
        }
        const now = performance.now();
        const from = now - WINDOW_MS;
        const windowSec = WINDOW_MS / 1000;
        const slots = new Set<number>();
        const slotsPerSecByStream: Record<string, number> = {};
        let total = 0;
        for (const [video, samples] of this.presentsByVideo) {
            const kept = samples.filter(s => s.displayAt >= from);
            this.presentsByVideo.set(video, kept);
            total += kept.length;
            for (const s of kept)
                slots.add(Math.floor(s.displayAt / SLOT_BIN_MS));

            const streamId = video.dataset.streamId;
            if (streamId)
                slotsPerSecByStream[streamId] = ratePerSec(kept, windowSec);
        }

        return {
            presentsPerSec: total / windowSec,
            slotsPerSec: slots.size / windowSec,
            videoCount: this.watched.size,
            unobservableCount: this.unobservableCount,
            slotsPerSecByStream,
        };
    }

    // Private methods

    private onRescanTick(): void {
        if (performance.now() - this.lastCollectAt > IDLE_STOP_MS) {
            this.stop();
            return;
        }

        this.rescan();
    }

    private rescan(): void {
        const live = new Set<HTMLVideoElement>();
        let unobservable = 0;
        // Live tiles only (remote players + self-preview) — a plain document-wide
        // 'video' query counted attachment/content videos as unobservable tiles.
        for (const el of document.querySelectorAll<HTMLVideoElement>('video.live-stream-video')) {
            const video = el;
            if (video.srcObject instanceof MediaStream
                && typeof video.requestVideoFrameCallback === 'function')
                live.add(video);
            else
                unobservable++;
        }
        this.unobservableCount = unobservable;
        for (const [video, handle] of this.watched) {
            if (!live.has(video)) {
                try { video.cancelVideoFrameCallback(handle); } catch { /* ignore */ }
                this.watched.delete(video);
                this.presentsByVideo.delete(video);
            }
        }
        for (const video of live) {
            if (!this.watched.has(video))
                this.arm(video);
        }
    }

    private arm(video: HTMLVideoElement): void {
        this.presentsByVideo.set(video, []);
        const onFrame = (_now: DOMHighResTimeStamp, metadata: VideoFrameCallbackMetadata): void => {
            if (!this.watched.has(video))
                return;

            this.presentsByVideo.get(video)?.push({
                displayAt: metadata.expectedDisplayTime,
                presentedFrames: metadata.presentedFrames,
            });
            this.watched.set(video, video.requestVideoFrameCallback(onFrame));
        };
        this.watched.set(video, video.requestVideoFrameCallback(onFrame));
    }

    private stop(): void {
        for (const [video, handle] of this.watched) {
            try { video.cancelVideoFrameCallback(handle); } catch { /* ignore */ }
        }
        this.watched.clear();
        this.presentsByVideo.clear();
        this.unobservableCount = 0;
        if (this.rescanTimer !== null) {
            clearInterval(this.rescanTimer);
            this.rescanTimer = null;
        }
    }
}

const meter = new PresentRateMeter();

// Lazy-start on first call; self-stops (rVFC + rescan released) once the
// diagnostics modal stops polling for IDLE_STOP_MS.
export function collectPresentRate(): PresentRateSnapshot {
    return meter.collect();
}

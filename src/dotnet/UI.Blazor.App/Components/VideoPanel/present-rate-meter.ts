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
    // Per-stream display rate (distinct display slots/sec) for <video> elements
    // carrying data-stream-id — the honest "what the eye sees" number, unlike
    // the per-stream throughput counter which is the MSTG write rate.
    slotsPerSecByStream: Record<string, number>;
}

class PresentRateMeter {
    private readonly watched = new Map<HTMLVideoElement, number>();
    private readonly presentsByVideo = new Map<HTMLVideoElement, number[]>();
    private rescanTimer: ReturnType<typeof setInterval> | null = null;
    private lastCollectAt = 0;

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
        for (const [video, times] of this.presentsByVideo) {
            const kept = times.filter(t => t >= from);
            this.presentsByVideo.set(video, kept);
            total += kept.length;
            const streamSlots = new Set<number>();
            for (const t of kept) {
                const slot = Math.floor(t / SLOT_BIN_MS);
                slots.add(slot);
                streamSlots.add(slot);
            }
            const streamId = video.dataset.streamId;
            if (streamId)
                slotsPerSecByStream[streamId] = streamSlots.size / windowSec;
        }

        return {
            presentsPerSec: total / windowSec,
            slotsPerSec: slots.size / windowSec,
            videoCount: this.watched.size,
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
        for (const el of document.querySelectorAll('video')) {
            const video = el;
            if (video.srcObject instanceof MediaStream
                && typeof video.requestVideoFrameCallback === 'function')
                live.add(video);
        }
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

            this.presentsByVideo.get(video)?.push(metadata.expectedDisplayTime);
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

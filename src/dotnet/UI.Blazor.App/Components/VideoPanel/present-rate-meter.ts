// Diagnostics-only union present-rate meter. Watches every live-stream
// <video> element (remote tiles + self-preview) via requestVideoFrameCallback
// and reports how many distinct display slots per second the union of them
// touches — the leading indicator for full-screen WebView redraw rate that
// present-grid.ts exists to minimize. presentsPerSec stays ≈ sum of stream
// fps; slotsPerSec drops to ≈ grid rate when alignment works.

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
}

class PresentRateMeter {
    private readonly watched = new Map<HTMLVideoElement, number>();
    private presentTimes: number[] = [];
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
        this.presentTimes = this.presentTimes.filter(t => t >= from);
        const slots = new Set<number>();
        for (const t of this.presentTimes)
            slots.add(Math.floor(t / SLOT_BIN_MS));
        const windowSec = WINDOW_MS / 1000;
        return {
            presentsPerSec: this.presentTimes.length / windowSec,
            slotsPerSec: slots.size / windowSec,
            videoCount: this.watched.size,
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
            }
        }
        for (const video of live) {
            if (!this.watched.has(video))
                this.arm(video);
        }
    }

    private arm(video: HTMLVideoElement): void {
        const onFrame = (_now: DOMHighResTimeStamp, metadata: VideoFrameCallbackMetadata): void => {
            if (!this.watched.has(video))
                return;
            this.presentTimes.push(metadata.expectedDisplayTime);
            this.watched.set(video, video.requestVideoFrameCallback(onFrame));
        };
        this.watched.set(video, video.requestVideoFrameCallback(onFrame));
    }

    private stop(): void {
        for (const [video, handle] of this.watched) {
            try { video.cancelVideoFrameCallback(handle); } catch { /* ignore */ }
        }
        this.watched.clear();
        this.presentTimes = [];
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

// Worker-side audio-clock-driven frame selector for the MSTG render path.
// Owns the decoded VideoFrame queue, picks the frame matching the audio
// clock, and writes it to a MediaStreamTrackGenerator's writable transferred
// from the main thread. Drops late frames; holds early frames; never bounces
// VideoFrames to main.
//
// Counterpart on main: VideoPlayer creates the MSTG, attaches its track to
// <video srcObject>, transfers writable + sync MessagePort here, then stops
// running its own render loop on the MSTG path.

import { AudioVideoSyncClient } from 'audio-video-sync-client';
import { getLogs } from 'logging';
import { BG_CANVAS_WIDTH, BG_DRAW_INTERVAL_MS, BG_FILTER } from '../services/bg-canvas-settings';

const { warnLog } = getLogs('VideoDecoder');

const SOFT_CATCHUP_FRAMES = 15;
const SOFT_CATCHUP_SPAN_MS = 600;
const SOFT_CATCHUP_KEEP_MS = 300;
const HARD_CAP_FRAMES = 30;

export interface BgPainter {
    canvas: OffscreenCanvas;
    ctx: OffscreenCanvasRenderingContext2D;
}

export class WorkerMstgSelector {
    private queue: VideoFrame[] = [];
    private readonly writer: WritableStreamDefaultWriter<VideoFrame>;
    private readonly syncClient: AudioVideoSyncClient;
    private writeInFlight = false;
    private lastWrittenTs = -1;
    private disposed = false;
    private lastBgDrawAtMs = 0;

    constructor(
        writable: WritableStream<VideoFrame>,
        syncPort: MessagePort,
        private readonly startedAtMs: number,
        private jitterBufferMs: number,
        private readonly bgPainter?: BgPainter,
    ) {
        this.writer = writable.getWriter();
        // tick() on every audio sync update — covers steady-state queue + advancing audio
        this.syncClient = new AudioVideoSyncClient(syncPort, () => this.tick());
    }

    onDecoded(frame: VideoFrame): void {
        if (this.disposed) {
            frame.close();
            return;
        }
        // Insert keeping queue sorted by timestamp (decoder may emit out-of-order
        // around B-frames, though we run in IPPP… so usually a no-op append).
        const ts = frame.timestamp;
        let i = this.queue.length;
        while (i > 0 && this.queue[i - 1].timestamp > ts) i--;
        this.queue.splice(i, 0, frame);
        this.applyBackpressure();
        this.tick();
    }

    setJitterBufferMs(ms: number): void {
        this.jitterBufferMs = ms;
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        for (const f of this.queue) {
            try { f.close(); } catch { /* ignore */ }
        }
        this.queue = [];
        try { void this.writer.close(); } catch { /* ignore */ }
    }

    private applyBackpressure(): void {
        // Soft catchup: drop oldest frames when buffer span exceeds 600 ms.
        if (this.queue.length > SOFT_CATCHUP_FRAMES) {
            const spanMs = (this.queue[this.queue.length - 1].timestamp - this.queue[0].timestamp) / 1000;
            if (spanMs > SOFT_CATCHUP_SPAN_MS) {
                const cutoffUs = this.queue[this.queue.length - 1].timestamp - SOFT_CATCHUP_KEEP_MS * 1000;
                while (this.queue.length > 1 && this.queue[0].timestamp < cutoffUs) {
                    this.queue.shift()!.close();
                }
            }
        }
        while (this.queue.length > HARD_CAP_FRAMES) {
            this.queue.shift()!.close();
        }
    }

    private tick(): void {
        if (this.disposed || this.writeInFlight || this.queue.length === 0) return;

        let targetUs: number;
        const state = this.syncClient.get();
        if (state) {
            const audioPlayingAtMs = this.syncClient.interpolatePlayingAt() * 1000;
            const targetMs = (state.recordedAtMs - this.startedAtMs) + audioPlayingAtMs;
            targetUs = targetMs * 1000;
        } else {
            // No audio sync state → write the newest frame ASAP (wall-clock fallback).
            targetUs = this.queue[this.queue.length - 1].timestamp;
        }
        const adjustedTargetUs = targetUs - this.jitterBufferMs * 1000;

        // Pick the latest eligible frame, dropping all earlier ones.
        let eligible: VideoFrame | null = null;
        while (this.queue.length > 0 && this.queue[0].timestamp <= adjustedTargetUs) {
            if (eligible) eligible.close();
            eligible = this.queue.shift()!;
        }
        if (!eligible) return;
        if (eligible.timestamp === this.lastWrittenTs) {
            eligible.close();
            return;
        }

        this.lastWrittenTs = eligible.timestamp;

        // Paint bg backdrop from the same VideoFrame, throttled to ~10 fps.
        // Drawing happens before writer.write so the frame is still readable
        // — writer takes ownership when its promise resolves.
        if (this.bgPainter) {
            const nowMs = performance.now();
            if (nowMs - this.lastBgDrawAtMs >= BG_DRAW_INTERVAL_MS) {
                this.lastBgDrawAtMs = nowMs;
                try {
                    const dw = eligible.displayWidth || 1;
                    const dh = eligible.displayHeight || 1;
                    const bgH = Math.max(1, Math.round(BG_CANVAS_WIDTH * dh / dw));
                    if (this.bgPainter.canvas.width !== BG_CANVAS_WIDTH ||
                        this.bgPainter.canvas.height !== bgH) {
                        this.bgPainter.canvas.width = BG_CANVAS_WIDTH;
                        this.bgPainter.canvas.height = bgH;
                        // Canvas resize resets ctx state — re-apply filter + smoothing.
                        this.bgPainter.ctx.imageSmoothingEnabled = false;
                        this.bgPainter.ctx.filter = BG_FILTER;
                    }
                    this.bgPainter.ctx.drawImage(eligible, 0, 0, BG_CANVAS_WIDTH, bgH);
                } catch (e) {
                    warnLog?.log('Bg paint failed:', e);
                }
            }
        }

        this.writeInFlight = true;
        this.writer.write(eligible)
            .catch((e: unknown) => {
                if (!this.disposed) warnLog?.log('MSTG worker write failed:', e);
            })
            .finally(() => {
                this.writeInFlight = false;
                if (!this.disposed && this.queue.length > 0) this.tick();
            });
    }
}

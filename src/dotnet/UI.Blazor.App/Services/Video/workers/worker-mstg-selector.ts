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
import { BG_BOX_BLUR_PASSES, BG_BOX_BLUR_RADIUS, BG_CANVAS_WIDTH, BG_DRAW_INTERVAL_MS } from '../services/bg-canvas-settings';

const { warnLog } = getLogs('VideoDecoder');

const SOFT_CATCHUP_FRAMES = 15;
const SOFT_CATCHUP_SPAN_MS = 600;
const SOFT_CATCHUP_KEEP_MS = 300;
const HARD_CAP_FRAMES = 30;

export interface BgPainter {
    canvas: OffscreenCanvas;
    ctx: OffscreenCanvasRenderingContext2D;
}

export interface WorkerMstgBufferStats {
    depth: number;
    spanMs: number;
}

export class WorkerMstgSelector {
    private queue: VideoFrame[] = [];
    private readonly writer: WritableStreamDefaultWriter<VideoFrame>;
    private readonly syncClient: AudioVideoSyncClient;
    private writeInFlight = false;
    private lastWrittenTs = -1;
    private disposed = false;
    private lastBgDrawAtMs = 0;
    // DIAG: throttled (~1 Hz) instrumentation to verify whether main-write and
    // bg-paint paths fire together. Confirms hypothesis that bg keeps painting
    // while main video freezes when the MSTG track downstream is broken.
    private lastDiagAtMs = 0;
    private bgPaintsSinceDiag = 0;
    private mainWritesSinceDiag = 0;
    private writeFailuresSinceDiag = 0;
    // Whether to paint the blur backdrop. Off for sidebar/unfocused tiles —
    // bg canvas is hidden by CSS in those states, so painting wastes CPU on
    // a 64×N readback + 3-pass box blur every 100 ms. Toggled from main via
    // decoder-worker.setBgPaintEnabled.
    private bgPaintEnabled = true;

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

    setBgPaintEnabled(enabled: boolean): void {
        this.bgPaintEnabled = enabled;
    }

    getBufferStats(): WorkerMstgBufferStats {
        const depth = this.queue.length;
        const spanMs = depth >= 2
            ? (this.queue[depth - 1].timestamp - this.queue[0].timestamp) / 1000
            : 0;
        return { depth, spanMs };
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
            const audioStartAtMs = state.recordedAtMs - state.playingAtSec * 1000;
            const targetMs = (audioStartAtMs - this.startedAtMs) + audioPlayingAtMs;
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
        // — writer takes ownership when its promise resolves. Blur is applied
        // via a portable software box-blur (see bgBoxBlur) — Safari
        // OffscreenCanvas silently ignores ctx.filter on some versions, so
        // we don't rely on it. Box blur on 64×N at 10 fps is microseconds.
        if (this.bgPainter && this.bgPaintEnabled) {
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
                        // Canvas resize resets ctx state — re-apply smoothing.
                        this.bgPainter.ctx.imageSmoothingEnabled = false;
                    }
                    this.bgPainter.ctx.drawImage(eligible, 0, 0, BG_CANVAS_WIDTH, bgH);
                    bgBoxBlur(this.bgPainter.ctx, BG_CANVAS_WIDTH, bgH,
                        BG_BOX_BLUR_RADIUS, BG_BOX_BLUR_PASSES);
                    this.bgPaintsSinceDiag++;
                } catch (e) {
                    warnLog?.log('Bg paint failed:', e);
                }
            }
        }

        this.writeInFlight = true;
        this.writer.write(eligible)
            .then(() => { this.mainWritesSinceDiag++; })
            .catch((e: unknown) => {
                this.writeFailuresSinceDiag++;
                if (!this.disposed) warnLog?.log('MSTG worker write failed:', e);
            })
            .finally(() => {
                this.writeInFlight = false;
                if (!this.disposed && this.queue.length > 0) this.tick();
            });

        // DIAG: emit a 1 Hz summary so we can correlate main-write activity vs bg-paint activity.
        // If bg keeps incrementing but mainWrites stalls (or writeFailures climbs), we have
        // evidence that the MSTG-side path is broken while the decoder + worker are healthy.
        const diagNowMs = performance.now();
        if (diagNowMs - this.lastDiagAtMs >= 1000) {
            this.lastDiagAtMs = diagNowMs;
            warnLog?.log(
                `MstgSelector DIAG: queueDepth=${this.queue.length}, writeInFlight=${this.writeInFlight}, ` +
                `bgPaints/s=${this.bgPaintsSinceDiag}, mainWrites/s=${this.mainWritesSinceDiag}, ` +
                `writeFailures/s=${this.writeFailuresSinceDiag}, hasBgPainter=${!!this.bgPainter}`);
            this.bgPaintsSinceDiag = 0;
            this.mainWritesSinceDiag = 0;
            this.writeFailuresSinceDiag = 0;
        }
    }
}

// Portable separable box blur on an OffscreenCanvas 2D context. Multiple
// passes approximate Gaussian (3 passes ≈ Gaussian σ ≈ radius). Operates
// on a 64×N image at 10 fps — total ≈ 200k ops/draw, ~2M/s, microseconds.
// Replaces ctx.filter='blur(...)' which Safari OffscreenCanvas silently
// drops on iOS ≤17.3 / iPadOS, leaving the bg pixelated.
function bgBoxBlur(
    ctx: OffscreenCanvasRenderingContext2D,
    w: number, h: number,
    radius: number, passes: number,
): void {
    if (radius < 1 || passes < 1 || w < 1 || h < 1) return;
    const imageData = ctx.getImageData(0, 0, w, h);
    const data = imageData.data;
    const tmp = new Uint8ClampedArray(data.length);
    const wPx = w << 2;
    for (let p = 0; p < passes; p++) {
        // Horizontal pass: data → tmp
        for (let y = 0; y < h; y++) {
            const rowStart = y * wPx;
            for (let x = 0; x < w; x++) {
                let r = 0, g = 0, b = 0, n = 0;
                const xMin = Math.max(0, x - radius);
                const xMax = Math.min(w - 1, x + radius);
                for (let xi = xMin; xi <= xMax; xi++) {
                    const i = rowStart + (xi << 2);
                    r += data[i]; g += data[i + 1]; b += data[i + 2];
                    n++;
                }
                const i = rowStart + (x << 2);
                tmp[i] = r / n; tmp[i + 1] = g / n; tmp[i + 2] = b / n; tmp[i + 3] = 255;
            }
        }
        // Vertical pass: tmp → data
        for (let x = 0; x < w; x++) {
            const colStart = x << 2;
            for (let y = 0; y < h; y++) {
                let r = 0, g = 0, b = 0, n = 0;
                const yMin = Math.max(0, y - radius);
                const yMax = Math.min(h - 1, y + radius);
                for (let yi = yMin; yi <= yMax; yi++) {
                    const i = yi * wPx + colStart;
                    r += tmp[i]; g += tmp[i + 1]; b += tmp[i + 2];
                    n++;
                }
                const i = y * wPx + colStart;
                data[i] = r / n; data[i + 1] = g / n; data[i + 2] = b / n; data[i + 3] = 255;
            }
        }
    }
    ctx.putImageData(imageData, 0, 0);
}

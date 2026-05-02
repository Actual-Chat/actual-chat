// Worker-side wallclock-driven frame selector for the MSTG render path.
// Holds a single decoded VideoFrame slot (the doc's `video presentation`
// replaceable slot — docs/video-pipeline.md), writes the next decoded frame
// to a MediaStreamTrackGenerator's writable transferred from the main thread.
// New decode replaces pending; never bounces VideoFrames to main.
//
// Counterpart on main: VideoPlayer creates the MSTG, attaches its track to
// <video srcObject>, transfers writable here, then stops running its own
// render loop on the MSTG path.

import { getLogs } from 'logging';
import { BG_BLUR_STRENGTH, BG_DRAW_INTERVAL_MS } from '../services/bg-canvas-settings';
import type { BgBlurRenderer } from '../webgpu-blur';

const { warnLog } = getLogs('VideoDecoder');

// Single decoded-frame slot. Jitter absorption + pacing happen upstream in
// the decoder worker's encoded pre-decode buffer (the doc's `video buffer`);
// the decoder pulls at wallclock rate. Once a frame is decoded it's the
// most-recent presentation candidate — older pending frames are stale by
// definition. New decode replaces pending.

export interface BgPainter {
    renderer: BgBlurRenderer;
}

export interface WorkerMstgBufferStats {
    depth: number;
    spanMs: number;
}

export class WorkerMstgSelector {
    private pending: VideoFrame | null = null;
    private readonly writer: WritableStreamDefaultWriter<VideoFrame>;
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
    // bg canvas is hidden by CSS in those states, so painting wastes GPU work
    // and keeps the WebGPU pipeline hot. Toggled from main via
    // decoder-worker.setBgPaintEnabled.
    private bgPaintEnabled = true;

    resetPrimming(): void {
        this.lastWrittenTs = -1;
    }

    constructor(
        writable: WritableStream<VideoFrame>,
        private readonly startedAtMs: number,
        private readonly serverClockOffsetMs: number,
        private jitterBufferMs: number,
        private readonly bgPainter?: BgPainter,
    ) {
        this.writer = writable.getWriter();
    }

    onDecoded(frame: VideoFrame): void {
        if (this.disposed) {
            frame.close();
            return;
        }
        // Dumb as fuck mode: always replace and tick immediately.
        if (this.pending) {
            try { this.pending.close(); } catch { /* already closed */ }
        }
        this.pending = frame;
        this.tick();
    }

    setJitterBufferMs(ms: number): void {
        this.jitterBufferMs = ms;
    }

    setBgPaintEnabled(enabled: boolean): void {
        this.bgPaintEnabled = enabled;
    }

    // Wallclock target in microseconds (frame-stream coords). Used by the
    // encoded pre-decode buffer's drain loop to pace decoding.
    getAudioTargetUs(): number | null {
        const serverNowMs = Date.now() + this.serverClockOffsetMs;
        return (serverNowMs - this.startedAtMs) * 1000;
    }

    getBufferStats(): WorkerMstgBufferStats {
        // Single-slot selector — depth ∈ {0, 1}, span always 0. Real
        // playback buffer is the encoded ring buffer in decoder-worker.
        return { depth: this.pending ? 1 : 0, spanMs: 0 };
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        if (this.pending) {
            try { this.pending.close(); } catch { /* ignore */ }
            this.pending = null;
        }
        try { void this.writer.close(); } catch { /* ignore */ }
    }

    private tick(): void {
        if (this.disposed) return;
        if (this.writeInFlight || !this.pending) {
            return;
        }

        // Dumb as fuck mode: ignore all timing rules and just output what we have.
        const eligible = this.pending;
        this.pending = null;

        if (eligible.timestamp === this.lastWrittenTs) {
            eligible.close();
            return;
        }

        this.lastWrittenTs = eligible.timestamp;

        // Paint bg backdrop from the same VideoFrame, throttled to ~10 fps.
        // Drawing happens before writer.write so the frame is still readable
        // — writer takes ownership when its promise resolves. The renderer
        // runs a WebGPU dual-Kawase blur directly into the bg canvas swap
        // chain (no CPU readback). Canvas drawing-buffer size is left at
        // whatever the host set; CSS upscales the swap-chain bitmap to fill
        // the parent via `object-fit: cover`. On the first call WebGPU init
        // is in flight and render() returns false; subsequent calls paint.
        if (this.bgPainter && this.bgPaintEnabled) {
            const nowMs = performance.now();
            if (nowMs - this.lastBgDrawAtMs >= BG_DRAW_INTERVAL_MS) {
                this.lastBgDrawAtMs = nowMs;
                try {
                    if (this.bgPainter.renderer.render(eligible, BG_BLUR_STRENGTH))
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
                if (this.disposed) return;
                if (this.pending) this.tick();
            });

        // DIAG: emit a 1 Hz summary of main-write activity vs bg-paint activity.
        // If bg keeps incrementing but mainWrites stalls (or writeFailures climbs),
        // the MSTG-side path is broken while the decoder + worker are healthy.
        const diagNowMs = performance.now();
        if (diagNowMs - this.lastDiagAtMs >= 1000) {
            this.lastDiagAtMs = diagNowMs;
            const lastWrittenMs = this.lastWrittenTs >= 0 ? this.lastWrittenTs / 1000 : 0;
            warnLog?.log(
                `MstgSelector DIAG: ` +
                `lastWrittenMs=${lastWrittenMs.toFixed(0)}, writeInFlight=${this.writeInFlight}, ` +
                `bgPaints/s=${this.bgPaintsSinceDiag}, mainWrites/s=${this.mainWritesSinceDiag}, ` +
                `writeFailures/s=${this.writeFailuresSinceDiag}, hasBgPainter=${!!this.bgPainter}`);
            this.bgPaintsSinceDiag = 0;
            this.mainWritesSinceDiag = 0;
            this.writeFailuresSinceDiag = 0;
        }
    }
}

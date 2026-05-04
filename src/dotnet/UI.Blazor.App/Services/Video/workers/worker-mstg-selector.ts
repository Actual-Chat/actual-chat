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
import { ReplaceableSlot } from 'buffers';

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
    private readonly pending = new ReplaceableSlot<VideoFrame>({
        dispose: frame => {
            try { frame.close(); } catch { /* already closed */ }
        },
    });
    private readonly writer: WritableStreamDefaultWriter<VideoFrame>;
    private writeInFlight = false;
    private lastWrittenTs = -1;
    // Coded dims of the last frame whose writer.write resolved. This is the
    // dim the downstream MSTG track has actually consumed, so it lines up
    // with `<video>.videoWidth/Height` once the element renders the frame.
    // Surfaced via getLastWrittenSize() and used as the verification
    // reference (instead of the just-emitted decoder dim, which races video
    // element propagation by ≥1 frame during simulcast tier swaps).
    private lastWrittenWidth = 0;
    private lastWrittenHeight = 0;
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
        // Always replace and tick immediately.
        this.pending.push(frame);
        this.tick();
    }

    setJitterBufferMs(ms: number): void {
        this.jitterBufferMs = ms;
    }

    setBgPaintEnabled(enabled: boolean): void {
        this.bgPaintEnabled = enabled;
    }

    getBufferStats(): WorkerMstgBufferStats {
        // Single-slot selector — depth ∈ {0, 1}, span always 0. Real
        // playback buffer is the encoded ring buffer in decoder-worker.
        return { depth: this.pending.length, spanMs: 0 };
    }

    getLastWrittenOffsetMs(): number | undefined {
        return this.lastWrittenTs >= 0
            ? this.lastWrittenTs / 1000
            : undefined;
    }

    getLastWrittenSize(): { width: number; height: number } | null {
        return this.lastWrittenWidth > 0 && this.lastWrittenHeight > 0
            ? { width: this.lastWrittenWidth, height: this.lastWrittenHeight }
            : null;
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.pending.clear();
        try { void this.writer.close(); } catch { /* ignore */ }
    }

    private tick(): void {
        if (this.disposed) return;
        if (this.writeInFlight || this.pending.isEmpty()) {
            return;
        }

        // Ignore all timing rules and just output what we have.
        const eligible = this.pending.take()!;

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
        // Capture coded dims BEFORE writer.write — write transfers ownership
        // (the writer closes the frame), so reading codedWidth after the
        // call may hit a closed frame.
        const writtenW = eligible.codedWidth;
        const writtenH = eligible.codedHeight;
        this.writer.write(eligible)
            .then(() => {
                this.mainWritesSinceDiag++;
                if (writtenW > 0 && writtenH > 0) {
                    this.lastWrittenWidth = writtenW;
                    this.lastWrittenHeight = writtenH;
                }
            })
            .catch((e: unknown) => {
                this.writeFailuresSinceDiag++;
                if (!this.disposed) warnLog?.log('MSTG worker write failed:', e);
            })
            .finally(() => {
                this.writeInFlight = false;
                if (this.disposed) return;
                if (!this.pending.isEmpty()) this.tick();
            });

        // DIAG: emit a 1 Hz summary of main-write activity vs bg-paint activity.
        // If bg keeps incrementing but mainWrites stalls (or writeFailures climbs),
        // the MSTG-side path is broken while the decoder + worker are healthy.
        const diagNowMs = performance.now();
        if (diagNowMs - this.lastDiagAtMs >= 1000) {
            this.lastDiagAtMs = diagNowMs;
            const lastWrittenMs = this.lastWrittenTs >= 0 ? this.lastWrittenTs / 1000 : 0;
            warnLog?.log(
                `tick: ` +
                `lastWrittenMs=${lastWrittenMs.toFixed(0)}, writeInFlight=${this.writeInFlight}, ` +
                `bgPaints/s=${this.bgPaintsSinceDiag}, mainWrites/s=${this.mainWritesSinceDiag}, ` +
                `writeFailures/s=${this.writeFailuresSinceDiag}, hasBgPainter=${!!this.bgPainter}`);
            this.bgPaintsSinceDiag = 0;
            this.mainWritesSinceDiag = 0;
            this.writeFailuresSinceDiag = 0;
        }
    }
}

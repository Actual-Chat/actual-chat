// Worker-side audio-clock-driven frame selector for the MSTG render path.
// Holds a single decoded VideoFrame slot (the doc's `video presentation`
// replaceable slot — docs/video-pipeline.md), picks it for the writer when
// it's at or behind the audio target, and writes it to a
// MediaStreamTrackGenerator's writable transferred from the main thread.
// Drops late frames; holds early frames; never bounces VideoFrames to main.
//
// Counterpart on main: VideoPlayer creates the MSTG, attaches its track to
// <video srcObject>, transfers writable + sync MessagePort here, then stops
// running its own render loop on the MSTG path.

import { AudioVideoSyncClient } from 'audio-video-sync-client';
import { getLogs } from 'logging';
import { BG_BLUR_STRENGTH, BG_DRAW_INTERVAL_MS } from '../services/bg-canvas-settings';
import type { BgBlurRenderer } from '../webgpu-blur';

const { warnLog } = getLogs('VideoDecoder');

// Single decoded-frame slot. Jitter absorption + audio-target pacing now
// happen upstream in the decoder worker's encoded pre-decode buffer
// (the doc's `video buffer`); the decoder pulls at audio rate. Once a
// frame is decoded it's the most-recent presentation candidate — older
// pending frames are stale by definition. New decode replaces pending.
//
// Rule 4 force-write threshold: write the held frame when its ts is at
// least this far ahead of the current target — clock-skew safety so the
// MSTG track doesn't go `muted` after ~2 s of no writes when audio target
// can't catch up to decoded frames (rare; encoded drain usually handles).
const RULE4_FORCE_WRITE_AHEAD_US = 1_000_000;
// How many starved ticks before we emit a warn log (~1 s at 30 fps).
const STARVATION_WARN_TICKS = 30;

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
    // A/V sync DIAG state — last-tick snapshot for the 1 Hz summary log.
    // Lets us prove that audio-target tracks audio updates correctly, the
    // wallclock target advances at 1×, drift settles to a small constant
    // (≈ −audio-pipeline-latency), and the selector picks frames matching
    // the audio target.
    private lastTickAudioTgtMs = 0;
    private lastTickWcTgtMs = 0;
    private lastTickRawDriftUs = 0;
    private lastTickAudioStateAgeMs = -1;        // -1 means no audio state seen
    // Whether to paint the blur backdrop. Off for sidebar/unfocused tiles —
    // bg canvas is hidden by CSS in those states, so painting wastes GPU work
    // and keeps the WebGPU pipeline hot. Toggled from main via
    // decoder-worker.setBgPaintEnabled.
    private bgPaintEnabled = true;
    // Smoothed signed difference (us) between audio-anchored target and the
    // wallclock-anchored target. Positive → audio ahead of video → speed up.
    // Negative → audio behind → slow down. EMA over recent ticks.
    private smoothedDriftUs = 0;
    private hasObservedAudioState = false;
    // Idle tick fallback. Audio sync updates fire at ~5 Hz; onDecoded fires
    // ~30 fps. Both can stop (silent author, decoder stall). A self-scheduling
    // timeout keeps the wallclock target advancing during silence without
    // burning a 30 Hz timer per peer when nothing else is firing.
    private nextTickTimeout: ReturnType<typeof setTimeout> | null = null;
    private static readonly MAX_IDLE_TICK_MS = 100;
    // Rate-correction state (maintained for getDriftMs() reporters).
    private static readonly DRIFT_EMA_ALPHA = 0.15;
    // Counts consecutive ticks where the queue was non-empty but no frame
    // qualified AND Rule 4 chose to hold. Reset whenever a frame is written.
    private starvedTicks = 0;

    constructor(
        writable: WritableStream<VideoFrame>,
        syncPort: MessagePort,
        private readonly startedAtMs: number,
        private readonly serverClockOffsetMs: number,
        private jitterBufferMs: number,
        private readonly bgPainter?: BgPainter,
    ) {
        this.writer = writable.getWriter();
        // tick() on every audio sync update — covers steady-state queue + advancing audio
        this.syncClient = new AudioVideoSyncClient(syncPort, () => this.tick());
        // tick() periodically to drive the wallclock target even if no audio
        // updates arrive and no new frames decode (queue still has frames to
        // play out from buffer).
        this.scheduleNextTick(0);
    }

    onDecoded(frame: VideoFrame): void {
        if (this.disposed) {
            frame.close();
            return;
        }
        // Single-slot replace. Older pending frame is stale by definition —
        // the new decode is closer to the live edge and will line up better
        // with any future audio target. Decoder runs in IPPP order, so the
        // new frame's ts > pending's ts; no sort needed.
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

    // Returns smoothed audio-vs-wallclock drift in milliseconds, signed.
    // Positive → audio is ahead of video → speed up to converge.
    // Negative → video is ahead of audio → slow down. Zero before any audio
    // sync state has been observed.
    getDriftMs(): number {
        if (!this.hasObservedAudioState) return 0;
        return this.smoothedDriftUs / 1000;
    }

    getBufferStats(): WorkerMstgBufferStats {
        // Single-slot selector — depth ∈ {0, 1}, span always 0. Real
        // playback buffer is the encoded ring buffer in decoder-worker.
        return { depth: this.pending ? 1 : 0, spanMs: 0 };
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        if (this.nextTickTimeout !== null) {
            clearTimeout(this.nextTickTimeout);
            this.nextTickTimeout = null;
        }
        if (this.pending) {
            try { this.pending.close(); } catch { /* ignore */ }
            this.pending = null;
        }
        try { void this.writer.close(); } catch { /* ignore */ }
    }

    private scheduleNextTick(delayMs: number): void {
        if (this.disposed) return;
        if (this.nextTickTimeout !== null) clearTimeout(this.nextTickTimeout);
        this.nextTickTimeout = setTimeout(() => {
            this.nextTickTimeout = null;
            this.tick();
        }, delayMs);
    }

    private scheduleIdleTick(): void {
        this.scheduleNextTick(WorkerMstgSelector.MAX_IDLE_TICK_MS);
    }

    private tick(): void {
        if (this.disposed) return;
        if (this.writeInFlight || !this.pending) {
            this.scheduleIdleTick();
            return;
        }

        // ── Rule 1: wallclock-anchored target (always) ──────────────────────
        // Worker has no ServerClock; reconstruct it from the offset snapshot
        // taken on main thread at startPullInWorker time. Drift over a typical
        // call is < 50 ms — well within the ±100 ms correction band below.
        const serverNowMs = Date.now() + this.serverClockOffsetMs;
        const wallclockTargetUs = (serverNowMs - this.startedAtMs) * 1000;

        // ── Rule 2: audio is a soft correction signal, not a target ─────────
        // Compute audio-anchored target if state is available, derive drift,
        // smooth, clamp, apply as offset to the wallclock target. We never
        // jump backward when audio first appears — we instead start a slow
        // convergence (Rule 3 nudges <video>.playbackRate from main thread).
        //
        // Audio-target derivation:
        //   `state.recordedAtMs` is the audio TRACK ANCHOR — server-epoch ms
        //   when the audio sample at offset 0 was recorded. Constant for the
        //   whole audio session.
        //   `interpolatePlayingAt()` returns current playback offset in seconds
        //   since the track started.
        //   audio_now_wall_clock_ms = recordedAtMs + interpolatedPlayingAt*1000
        //   Map to video stream coordinates by subtracting `this.startedAtMs`.
        //
        //   ⚠ Earlier code computed `audioStartAtMs = recordedAtMs - playingAtSec*1000`
        //   then added `interpolatePlayingAt()` back. That cancelled
        //   `playingAtSec*1000` and left only `(perf.now() - capturedAt)*1000`,
        //   making audio-target ≈ constant (`recordedAtMs - startedAtMs +
        //   tiny`). Drift then grew linearly with wall-clock — visible in
        //   logs as `driftMs=-10773 → -12792 → -14803 …` (Δ ≈ 2 s per 2 s
        //   tick = the wall-clock advance). Don't subtract playingAtSec.
        let correctionUs = 0;
        const state = this.syncClient.get();
        // Snapshot for DIAG even if no state — keeps the log readable.
        this.lastTickWcTgtMs = wallclockTargetUs / 1000;
        this.lastTickAudioTgtMs = 0;
        this.lastTickRawDriftUs = 0;
        this.lastTickAudioStateAgeMs = -1;
        if (state) {
            const audioPlayingAtMs = this.syncClient.interpolatePlayingAt() * 1000;
            const audioTargetMs = state.recordedAtMs - this.startedAtMs + audioPlayingAtMs;
            const audioTargetUs = audioTargetMs * 1000;
            const rawDriftUs = audioTargetUs - wallclockTargetUs;
            const a = WorkerMstgSelector.DRIFT_EMA_ALPHA;
            this.smoothedDriftUs = this.hasObservedAudioState
                ? this.smoothedDriftUs * (1 - a) + rawDriftUs * a
                : rawDriftUs; // first sample: jump directly so getDriftMs reports a sane value immediately
            this.hasObservedAudioState = true;
            // No clamp here. Audio's playout pipeline can sit ~500–1000 ms behind
            // wallclock during cold-start (jitter buffer warming up). Clamping at
            // ±100 ms made it impossible to track that lag — the visible result
            // was video running up to 1 s ahead of the audio. We feed the full
            // smoothed drift through; Rule 3 (main-thread playbackRate nudge)
            // handles the slow convergence so the target shift isn't a jolt.
            correctionUs = this.smoothedDriftUs;
            this.lastTickAudioTgtMs = audioTargetMs;
            this.lastTickRawDriftUs = rawDriftUs;
            this.lastTickAudioStateAgeMs = performance.now() - state.capturedAt;
        }

        const targetUs = wallclockTargetUs + correctionUs;
        const adjustedTargetUs = targetUs - this.jitterBufferMs * 1000;

        // Single-slot pick. The early-return at the top of tick() guarantees
        // `pending` is non-null here. If it's at or behind the audio target,
        // write it. Otherwise hold — or force-write under Rule 4 (clock-skew
        // safety) if it sits ≥1 s ahead so the MSTG track doesn't go `muted`
        // after ~2 s of no writes. With the encoded pre-decode buffer pacing
        // upstream this should be rare (decoder runs at audio rate); kept
        // as defense against true clock skew.
        const pending = this.pending;
        let eligible: VideoFrame;
        if (pending.timestamp <= adjustedTargetUs
            || pending.timestamp > targetUs + RULE4_FORCE_WRITE_AHEAD_US) {
            eligible = pending;
            this.pending = null;
        } else {
            this.starvedTicks++;
            if (this.starvedTicks === STARVATION_WARN_TICKS) {
                warnLog?.log(
                    `MstgSelector starved ${this.starvedTicks} ticks: ` +
                    `pending held, aheadMs=${((pending.timestamp - targetUs) / 1000).toFixed(0)}, ` +
                    `targetUs=${targetUs}`);
            }
            this.scheduleIdleTick();
            return;
        }
        this.starvedTicks = 0;
        if (eligible.timestamp === this.lastWrittenTs) {
            eligible.close();
            this.scheduleIdleTick();
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
                else this.scheduleIdleTick();
            });

        // DIAG: emit a 1 Hz summary. Two purposes:
        //   1) Correlate main-write activity vs bg-paint activity — if bg keeps
        //      incrementing but mainWrites stalls (or writeFailures climbs),
        //      the MSTG-side path is broken while the decoder + worker are
        //      healthy.
        //   2) Prove A/V sync. We log the audio target, wallclock target, the
        //      raw and smoothed drift, the buffer span in ms, the actual
        //      "render-side residual" (audioTgt − lastWrittenTs/1000), and how
        //      fresh the audio state is (audioStateAge). With these values
        //      anyone can re-derive drift externally and confirm the formula.
        const diagNowMs = performance.now();
        if (diagNowMs - this.lastDiagAtMs >= 1000) {
            this.lastDiagAtMs = diagNowMs;
            const lastWrittenMs = this.lastWrittenTs >= 0 ? this.lastWrittenTs / 1000 : 0;
            // Render-side residual: how far ahead/behind the last frame we
            // actually wrote sits relative to the audio target. This is the
            // VISIBLE A/V mismatch — what a viewer would see/hear out of sync.
            const lastLagMs = this.lastTickAudioStateAgeMs >= 0 && this.lastWrittenTs >= 0
                ? this.lastTickAudioTgtMs - lastWrittenMs
                : 0;
            const audioTgtStr = this.lastTickAudioStateAgeMs >= 0
                ? `audioTgtMs=${this.lastTickAudioTgtMs.toFixed(0)}, ` +
                  `instantDriftMs=${(this.lastTickRawDriftUs / 1000).toFixed(0)}, ` +
                  `smoothedDriftMs=${(this.smoothedDriftUs / 1000).toFixed(0)}, ` +
                  `audioStateAgeMs=${this.lastTickAudioStateAgeMs.toFixed(0)}, ` +
                  `lastLagMs=${lastLagMs.toFixed(0)}, `
                : `audioTgt=NONE, `;
            warnLog?.log(
                `MstgSelector DIAG: ` +
                `wcTgtMs=${this.lastTickWcTgtMs.toFixed(0)}, ${audioTgtStr}` +
                `lastWrittenMs=${lastWrittenMs.toFixed(0)}, writeInFlight=${this.writeInFlight}, ` +
                `bgPaints/s=${this.bgPaintsSinceDiag}, mainWrites/s=${this.mainWritesSinceDiag}, ` +
                `writeFailures/s=${this.writeFailuresSinceDiag}, hasBgPainter=${!!this.bgPainter}`);
            this.bgPaintsSinceDiag = 0;
            this.mainWritesSinceDiag = 0;
            this.writeFailuresSinceDiag = 0;
        }
    }
}


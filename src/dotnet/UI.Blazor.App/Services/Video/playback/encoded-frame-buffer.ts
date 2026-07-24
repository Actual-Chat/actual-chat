import { RunningEMA } from 'math';
import type { ArrivedChunk, PlayerStats } from '../frame-envelopes';
import { FrameDropStage } from '../frame-drop-trace';

export type EncodedFrameBufferPushResult =
    | 'accepted'
    | 'droppedReset'
    | 'armed';

export type EncodedFrameBufferState = 'reset' | 'armed';

export interface EncodedFrameBufferOptions {
    targetSpanMs: number;
    /** Approximate duration of one source frame (ms). Used to attribute a
     *  duration to the trailing chunk in `spanMs()` (its real duration
     *  isn't known until the next chunk arrives). Production passes
     *  `VIDEO.frameDurationMs`. Defaults to `1000/30` (= 33.333 ms) for
     *  unit tests that don't initialize `VIDEO`. */
    frameDurationMs?: number;
    /** Per-stream stats; when supplied, push() samples `bufferUnderrunRatio`
     *  (EMA of 1 if spanMs() < targetSpanMs/3 else 0) while the buffer is
     *  armed. */
    stats?: PlayerStats;
    /** When the buffered span exceeds this, push() skips pre-decode to the
     *  newest buffered keyframe (drops the stale backlog). 0 disables.
     *  Should be well below the present-stage 4 s catch-up budget. */
    skipToLiveSpanMs?: number;
    /** Monotonic clock for the skip cooldown (ms). Defaults to performance.now. */
    nowFn?: () => number;
}

const UNDERRUN_RATIO_EMA_ALPHA = 0.1;
const UNDERRUN_FRACTION = 1 / 3;
const SKIP_TO_LIVE_COOLDOWN_MS = 1500;

// Receiver-side jitter buffer for encoded video chunks. Pacing is
// span-gated: tryPull() releases iff spanMs() >= targetSpanMs. An
// earlier capture-time-anchor design self-corrected drift poorly;
// span-gating self-corrects on every push/pull. Smoothing is the
// present stage's job (60 fps cap + catch-up skip).
//
// Reset semantics: deltas in 'reset' state are dropped (undecodable
// without their preceding keyframe); first keyframe transitions to
// 'armed'. reset() returns to 'reset' and disposes buffered chunks.
export class EncodedFrameBuffer {
    private readonly targetSpanMs: number;
    private readonly frameDurationMs: number;
    private readonly stats: PlayerStats | undefined;
    private readonly underrunEma: RunningEMA;
    private readonly underrunThresholdMs: number;
    private readonly chunks: ArrivedChunk[] = [];
    private state: EncodedFrameBufferState = 'reset';
    private readonly skipToLiveSpanMs: number;
    private readonly nowFn: () => number;
    private nextSkipAllowedAtMs = 0;
    private caOffsetMs: number | null = null;

    constructor(opts: EncodedFrameBufferOptions) {
        this.targetSpanMs = opts.targetSpanMs;
        this.frameDurationMs = opts.frameDurationMs ?? 1000 / 30;
        this.stats = opts.stats;
        this.skipToLiveSpanMs = opts.skipToLiveSpanMs ?? 0;
        this.nowFn = opts.nowFn ?? ((): number => performance.now());
        this.underrunEma = new RunningEMA(0, 1, UNDERRUN_RATIO_EMA_ALPHA);
        this.underrunThresholdMs = this.targetSpanMs * UNDERRUN_FRACTION;
    }

    count(): number {
        return this.chunks.length;
    }

    isReset(): boolean {
        return this.state === 'reset';
    }

    // Audio presentation capture-time in this stream's offset domain (ms).
    // When set, a skip targets the newest keyframe at or before it so video
    // lands on the audio timeline instead of the live edge. null ⇒ skip-to-live.
    setAudioCaptureOffsetMs(ms: number | null): void {
        this.caOffsetMs = ms;
    }

    audioCaptureOffsetMs(): number | null {
        return this.caOffsetMs;
    }

    // Capture-time duration of buffered content (ms). The trailing-chunk's
    // own duration isn't known until the next chunk arrives, so it's
    // approximated as one nominal frame. 0 only when the buffer is empty.
    spanMs(): number {
        const n = this.chunks.length;
        if (n === 0) return 0;
        const first = this.chunks[0];
        const last = this.chunks[n - 1];
        const span = last.capturedAt.timeMs - first.capturedAt.timeMs;
        return Math.max(0, span) + this.frameDurationMs;
    }

    isReady(): boolean {
        if (this.state !== 'armed') return false;
        if (this.chunks.length === 0) return false;
        return this.spanMs() >= this.targetSpanMs;
    }

    push(chunk: ArrivedChunk): EncodedFrameBufferPushResult {
        if (this.state === 'reset') {
            if (!chunk.isKeyFrame) {
                this.disposeChunk(chunk);
                return 'droppedReset';
            }
            this.state = 'armed';
            this.chunks.push(chunk);
            this.sampleUnderrun();
            return 'armed';
        }
        this.chunks.push(chunk);
        this.sampleUnderrun();
        this.trySkipToLive();
        return 'accepted';
    }

    // Pre-decode skip: when the buffered span exceeds skipToLiveSpanMs, drop
    // every chunk before the chosen keyframe. Target is the newest keyframe at
    // or before the audio capture-point (caOffsetMs) so video lands on the
    // audio timeline; with no audio offset it's the newest keyframe (live edge).
    // Keyframe-anchored (decoder stays valid); cooldown prevents thrash. No-op
    // when disabled, under threshold, in cooldown, when audio sits before the
    // head (nothing at/before Ca past the head), or when the target keyframe is
    // already the head.
    private trySkipToLive(): void {
        if (this.skipToLiveSpanMs <= 0 || this.state !== 'armed')
            return;
        if (this.spanMs() <= this.skipToLiveSpanMs)
            return;
        const now = this.nowFn();
        if (now < this.nextSkipAllowedAtMs)
            return;

        // Newest keyframe at or before Ca (newest overall when Ca is unset);
        // only worth skipping if it's past the head.
        let kfIndex = -1;
        for (let i = this.chunks.length - 1; i > 0; i--) {
            if (!this.chunks[i].isKeyFrame)
                continue;
            if (this.caOffsetMs !== null && this.chunks[i].capturedAt.timeMs > this.caOffsetMs)
                continue;
            kfIndex = i;
            break;
        }
        if (kfIndex <= 0)
            return;

        for (let i = 0; i < kfIndex; i++)
            this.disposeChunk(this.chunks[i]);
        this.chunks.splice(0, kfIndex);
        this.nextSkipAllowedAtMs = now + SKIP_TO_LIVE_COOLDOWN_MS;
        this.countDrops(FrameDropStage.ReceiverSkipToLive, kfIndex);
    }

    private countDrops(stage: FrameDropStage, n: number): void {
        const stats = this.stats;
        if (!stats || n <= 0) return;
        stats.dropTrace.set(stage, (stats.dropTrace.get(stage) ?? 0) + n);
    }

    private sampleUnderrun(): void {
        const stats = this.stats;
        if (!stats) return;
        const underrun = this.spanMs() < this.underrunThresholdMs ? 1 : 0;
        this.underrunEma.appendSample(underrun);
        stats.bufferUnderrunRatio = this.underrunEma.value;
    }

    tryPull(): ArrivedChunk | null {
        if (!this.isReady()) return null;
        const chunk = this.chunks.shift() ?? null;
        if (chunk && this.stats)
            this.stats.lastBufferPullAtMs = Date.now();
        return chunk;
    }

    reset(): void {
        for (const chunk of this.chunks) {
            this.disposeChunk(chunk);
        }
        this.chunks.length = 0;
        this.state = 'reset';
    }

    // Stateless helper: the buffer itself doesn't reject regressing chunks.
    detectRegression(chunk: ArrivedChunk, toleranceMs: number): boolean {
        const n = this.chunks.length;
        if (n === 0) return false;
        const last = this.chunks[n - 1];
        // capturedAt across epochs is incomparable.
        if (last.capturedAt.epoch !== chunk.capturedAt.epoch) return false;
        const delta = last.capturedAt.timeMs - chunk.capturedAt.timeMs;
        return delta > toleranceMs;
    }

    private disposeChunk(chunk: ArrivedChunk): void {
        const maybeDisposable = chunk as unknown as { dispose?: () => void };
        if (typeof maybeDisposable.dispose === 'function') {
            try { maybeDisposable.dispose(); } catch { /* ignore */ }
            return;
        }
        closeEncodedChunk(chunk.chunk);
    }
}

function closeEncodedChunk(chunk: EncodedVideoChunk): void {
    const close = (chunk as unknown as { close?: () => void }).close;
    if (typeof close !== 'function') return;
    try { close.call(chunk); } catch { /* ignore */ }
}

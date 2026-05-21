import { RunningEMA } from 'math';
import type { ArrivedChunk, PlayerStats } from '../frame-envelopes';

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
}

const UNDERRUN_RATIO_EMA_ALPHA = 0.1;
const UNDERRUN_FRACTION = 1 / 3;

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

    constructor(opts: EncodedFrameBufferOptions) {
        this.targetSpanMs = opts.targetSpanMs;
        this.frameDurationMs = opts.frameDurationMs ?? 1000 / 30;
        this.stats = opts.stats;
        this.underrunEma = new RunningEMA(0, 1, UNDERRUN_RATIO_EMA_ALPHA);
        this.underrunThresholdMs = this.targetSpanMs * UNDERRUN_FRACTION;
    }

    count(): number {
        return this.chunks.length;
    }

    isReset(): boolean {
        return this.state === 'reset';
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
        return 'accepted';
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
        return this.chunks.shift() ?? null;
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

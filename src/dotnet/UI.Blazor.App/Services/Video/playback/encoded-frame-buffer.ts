import type { ArrivedChunk } from '../frame-envelopes';

// ---- Types ---------------------------------------------------------------

/**
 * Outcome of `EncodedFrameBuffer.push(chunk)`. The receive-loop's stats
 * tap reads this to increment the right counter without having to peek
 * inside the buffer's state machine.
 */
export type EncodedFrameBufferPushResult =
    /** Chunk accepted into the queue. */
    | 'accepted'
    /** Delta dropped because the buffer is in `reset` state (waiting for
     *  the next keyframe to re-bootstrap). */
    | 'droppedReset'
    /** Keyframe accepted and transitioned the buffer from `reset` to
     *  `armed`. (Reported separately so the caller can log re-bootstrap
     *  events distinctly from steady-state appends.) */
    | 'armed';

/**
 * State machine:
 *   - `reset`   — initial state, and the state after `reset()`. Deltas
 *                 are dropped; the next keyframe transitions to `armed`.
 *   - `armed`   — keyframe seen; appending all chunks. `tryPull()` may
 *                 emit once span ≥ target AND the front chunk's pacing
 *                 deadline has arrived.
 */
export type EncodedFrameBufferState = 'reset' | 'armed';

export interface EncodedFrameBufferOptions {
    /**
     * Target span (ms) of buffered content before bootstrap fires.
     * `tryPull()` will not emit until `spanMs() >= targetSpanMs`. Once
     * armed, the same value is also added to each chunk's `arrivedAt`
     * to compute its pacing deadline (so the buffer maintains a
     * receiver-side cushion of approximately this duration).
     */
    targetSpanMs: number;

    /**
     * Wallclock source. Test override; defaults to `Date.now`. The
     * pacing math compares this against `chunk.arrivedAt.timeMs`, so
     * `now()` must be in the same domain (ms since Unix epoch, modulo
     * any monotonic-clock smoothing the receiver applies).
     */
    now?: () => number;
}

// ---- Class ---------------------------------------------------------------

/**
 * Receiver-side jitter buffer for encoded video chunks.
 *
 * Replaces the legacy `EncodedChunkBuffer` + the `sourceAnchorUs` /
 * `wallclockAnchorMs` / `waitingForKeyframe` / `skipFramesBeforeUs` state
 * tangle in `VideoOld/workers/decoder-worker.ts`. The pacing rule is
 * deliberately simpler than the legacy "anchor on first decode" scheme:
 *
 *   A chunk is "due" when `now() ≥ chunk.arrivedAt.timeMs + targetSpanMs`.
 *
 * In other words, every chunk waits in the buffer until it is at least
 * `targetSpanMs` old (in receiver-arrival time) before being released.
 * This makes pacing robust to sender clock anomalies — the buffer's
 * cushion is determined by RECEIVER arrival time, not by the sender's
 * `capturedAt` deltas. A network burst that delivers ten chunks in a
 * single millisecond just means all ten of them become eligible
 * `targetSpanMs` later, paced naturally by their already-staggered
 * `arrivedAt` stamps.
 *
 * Reset semantics: when `state === 'reset'`, deltas are dropped on push
 * (they'd be undecodable without their preceding keyframe). The first
 * keyframe transitions to `armed`. `reset()` returns to the `reset`
 * state, dropping all currently-buffered chunks.
 *
 * Standalone class — no operator scaffolding. Used by `epoch-reset.ts`
 * (calls `buffer.reset()` on epoch change) and `encoded-buffer.ts` (the
 * paced receiver-side operator that wraps it).
 */
export class EncodedFrameBuffer {
    private readonly targetSpanMs: number;
    private readonly nowFn: () => number;
    private readonly chunks: ArrivedChunk[] = [];
    private state: EncodedFrameBufferState = 'reset';

    constructor(opts: EncodedFrameBufferOptions) {
        this.targetSpanMs = opts.targetSpanMs;
        this.nowFn = opts.now ?? (() => Date.now());
    }

    // ---- Inspection ------------------------------------------------------

    count(): number {
        return this.chunks.length;
    }

    isReset(): boolean {
        return this.state === 'reset';
    }

    /**
     * Time-domain extent (ms) of buffered content. Defined as the gap
     * between the front chunk's `capturedAt.timeMs` and the back's. Zero
     * when fewer than two chunks are buffered.
     */
    spanMs(): number {
        const n = this.chunks.length;
        if (n < 2) return 0;
        const first = this.chunks[0];
        const last = this.chunks[n - 1];
        const span = last.capturedAt.timeMs - first.capturedAt.timeMs;
        return span > 0 ? span : 0;
    }

    /**
     * True iff a chunk is ready to be pulled RIGHT NOW. Equivalent to
     * `tryPull() !== null` but without taking the chunk.
     */
    isReady(): boolean {
        if (this.state !== 'armed') return false;
        if (this.chunks.length < 2) return false;
        if (this.spanMs() < this.targetSpanMs) return false;
        const front = this.chunks[0];
        const dueAt = front.arrivedAt.timeMs + this.targetSpanMs;
        return this.nowFn() >= dueAt;
    }

    // ---- Mutation --------------------------------------------------------

    /**
     * Push an arrived chunk. Returns the disposition:
     *   - `'droppedReset'` while in reset state and the chunk is a delta;
     *     the chunk is disposed.
     *   - `'armed'` when a keyframe arrives in reset state and
     *     transitions the buffer to armed.
     *   - `'accepted'` for any other steady-state append.
     */
    push(chunk: ArrivedChunk): EncodedFrameBufferPushResult {
        if (this.state === 'reset') {
            if (!chunk.isKeyFrame) {
                this.disposeChunk(chunk);
                return 'droppedReset';
            }
            this.state = 'armed';
            this.chunks.push(chunk);
            return 'armed';
        }
        this.chunks.push(chunk);
        return 'accepted';
    }

    /**
     * Return the next chunk if its scheduled decode time has arrived
     * AND the buffered span is at least `targetSpanMs`, else null.
     */
    tryPull(): ArrivedChunk | null {
        if (!this.isReady()) return null;
        const chunk = this.chunks.shift();
        return chunk ?? null;
    }

    /**
     * Drop everything; back to `reset` state. The next keyframe will
     * re-bootstrap. Buffered chunks are disposed; callers that need a
     * different disposal policy should drain via `tryPull()` first.
     */
    reset(): void {
        for (const chunk of this.chunks) {
            this.disposeChunk(chunk);
        }
        this.chunks.length = 0;
        this.state = 'reset';
    }

    // ---- Diagnostics -----------------------------------------------------

    /**
     * Returns true if `chunk.capturedAt.timeMs` regresses by more than
     * `toleranceMs` vs the most-recently-pushed chunk. Stateless helper
     * for the receive-loop (the buffer itself doesn't reject regressing
     * chunks — that's the caller's policy).
     */
    detectRegression(chunk: ArrivedChunk, toleranceMs: number): boolean {
        const n = this.chunks.length;
        if (n === 0) return false;
        const last = this.chunks[n - 1];
        // Ignore epoch boundaries — capturedAt across epochs is incomparable.
        if (last.capturedAt.epoch !== chunk.capturedAt.epoch) return false;
        const delta = last.capturedAt.timeMs - chunk.capturedAt.timeMs;
        return delta > toleranceMs;
    }

    // ---- Internals -------------------------------------------------------

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

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
 *                 emit once span ≥ target.
 */
export type EncodedFrameBufferState = 'reset' | 'armed';

export interface EncodedFrameBufferOptions {
    /**
     * Target span (ms) of buffered content. `tryPull()` only releases
     * when `spanMs() >= targetSpanMs`. The cushion sets the steady-state
     * buffer depth; the present stage drains at its own pace (60 fps cap
     * with optional catch-up skipping above the budget — see
     * `present-mstg.ts`).
     */
    targetSpanMs: number;
}

// ---- Class ---------------------------------------------------------------

/**
 * Receiver-side jitter buffer for encoded video chunks.
 *
 * Pacing rule — span-gated, no clock involved:
 *
 *   `tryPull()` releases the front chunk iff `spanMs() >= targetSpanMs`.
 *   That's the entire policy: when the buffer holds ≥ target span of
 *   capture-time content, drain; otherwise wait for more arrivals.
 *
 * Why no anchor / wallclock pacing: an earlier capture-time-anchor
 * design (release at `wallclockAnchor + (capturedAt - captureAnchor)`)
 * fixed the original burst-drop problem but introduced slow-drift —
 * any sustained period where capture rate exceeded the anchored release
 * pace caused the buffer to grow monotonically with no path to recover.
 * Span-gating self-corrects: every push that pushes span over target
 * unblocks a pull; every pull that drops span below target re-blocks
 * until the next push. The smoothing layer is no longer here — it's
 * the present stage's 60 fps cap + catch-up skip policy.
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
    private readonly chunks: ArrivedChunk[] = [];
    private state: EncodedFrameBufferState = 'reset';

    constructor(opts: EncodedFrameBufferOptions) {
        this.targetSpanMs = opts.targetSpanMs;
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
        if (this.chunks.length === 0) return false;
        return this.spanMs() >= this.targetSpanMs;
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
     * Return the next chunk if `spanMs() >= targetSpanMs`, else null.
     * Stateless beyond the queue itself — calling `tryPull()` repeatedly
     * drains the prefix until the residual span drops below target.
     */
    tryPull(): ArrivedChunk | null {
        if (!this.isReady()) return null;
        return this.chunks.shift() ?? null;
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

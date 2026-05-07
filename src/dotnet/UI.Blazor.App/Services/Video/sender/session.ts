// Sender-side session: long-lived resources reused across multiple
// recording runs.
//
// One `SenderSession` instance lives for the lifetime of the recorder
// worker. Within it, recording runs come and go (each is a
// `runStream(...)` invocation owned by the {@link Recorder} façade).
// State that belongs to a single run lives in operator closures and
// dies with the iterator chain; state that should survive across runs
// — the capture clock's monotonic continuity, the encoder pool's parked
// NVENC slot, the preview-track writer — lives here.
//
// `reset()` is called by the recorder between runs (e.g. after
// `restart()` swaps codecs); it tears down per-run state without
// touching the pooled encoders. `dispose()` is the full teardown
// invoked when the worker is shutting down.

import { MonotonicClock } from 'clocks';
import { EncoderPool, type EncoderPoolOptions } from './encoder-pool';

export interface PreviewGeneratorLike {
    /** Writable end of a `MediaStreamTrackGenerator` (or a fake in tests).
     *  The session takes a writer from this once at construction; if
     *  acquiring the writer throws the field stays `null` and the recorder
     *  treats preview as detached. */
    writable: WritableStream<VideoFrame>;
}

export interface SenderSessionOptions {
    /**
     * Optional preview track for the local self-view. If provided the
     * session locks a writer from `writable` and exposes it via
     * {@link SenderSession.previewWriter}; the recorder threads it into
     * the `previewTap` operator.
     */
    previewGenerator?: PreviewGeneratorLike;
    /** Test override. Default: `new MonotonicClock({ minTickMs: 33 })`. */
    createCaptureClock?: () => MonotonicClock;
    /** Test override / production tuning passed straight to {@link EncoderPool}. */
    encoderPoolOptions?: EncoderPoolOptions;
}

/**
 * Long-lived sender-side resource bag. Owned by the recorder worker.
 *
 * Field semantics:
 *   - `captureClock` — survives across runs so capture-time monotonicity
 *     is preserved across stop/start cycles. Operators (notably
 *     `stampCaptureTime`) read it.
 *   - `encoderPool` — codec-family-keyed pool with TTL park-after-release;
 *     reusing the parked entry across stop/start preserves NVENC slot
 *     ownership. See {@link EncoderPool} for the lifecycle.
 *   - `previewWriter` — the locked writer of an optional preview track.
 *     Null when no preview generator was supplied or when locking the
 *     writer threw at construction. Once obtained, the writer survives
 *     all runs; only `dispose()` releases it.
 */
export class SenderSession {
    readonly captureClock: MonotonicClock;
    readonly encoderPool: EncoderPool;
    readonly previewWriter: WritableStreamDefaultWriter<VideoFrame> | null;

    private disposed = false;

    constructor(opts: SenderSessionOptions = {}) {
        const createCaptureClock = opts.createCaptureClock
            ?? (() => new MonotonicClock({ minTickMs: 33 }));
        this.captureClock = createCaptureClock();
        this.encoderPool = new EncoderPool(opts.encoderPoolOptions);
        this.previewWriter = acquirePreviewWriter(opts.previewGenerator);
    }

    get isDisposed(): boolean { return this.disposed; }

    /**
     * Reset between recording runs. Per-run state lives in operator
     * closures (no session fields to clear), so this is a hook for
     * future cross-run resets that might land in this class. The
     * `encoderPool` is intentionally NOT touched — its whole reason
     * for existing is to survive between runs.
     */
    reset(): void {
        // No-op today. The recorder still calls this between runs so we
        // have a stable extension point if cross-run state lands here
        // later (e.g. cumulative stats aggregator, GPU device handle).
    }

    /**
     * Full teardown — disposes the encoder pool and releases the
     * preview writer. Idempotent: subsequent calls are no-ops.
     */
    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        try { this.encoderPool.dispose(); } catch { /* ignore */ }
        if (this.previewWriter) {
            try { this.previewWriter.releaseLock(); } catch { /* ignore */ }
        }
    }
}

function acquirePreviewWriter(
    generator: PreviewGeneratorLike | undefined,
): WritableStreamDefaultWriter<VideoFrame> | null {
    if (!generator) return null;
    try {
        return generator.writable.getWriter();
    } catch {
        // The generator's writable may already be locked, or the API
        // may be unavailable in this environment. Treat as "no preview".
        return null;
    }
}

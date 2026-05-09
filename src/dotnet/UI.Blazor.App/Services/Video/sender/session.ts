// Sender-side session: long-lived resources reused across recording runs.
// One instance per worker lifetime. Per-run state lives in operator closures;
// what survives across runs (capture-clock monotonicity, parked encoders,
// preview-track writer) lives here.

import { MonotonicClock } from 'clocks';
import { EncoderPool, type EncoderPoolOptions } from './encoder-pool';

export interface PreviewGeneratorLike {
    writable: WritableStream<VideoFrame>;
}

export interface SenderSessionOptions {
    previewGenerator?: PreviewGeneratorLike;
    createCaptureClock?: () => MonotonicClock;
    encoderPoolOptions?: EncoderPoolOptions;
}

// Pool TTL (5s) / 2: guarantees an idle session hits the eviction window
// at least once for every parked encoder so a HW slot can't be pinned forever.
const PoolSweepIntervalMs = 2_500;

export class SenderSession {
    readonly captureClock: MonotonicClock;
    readonly encoderPool: EncoderPool;
    readonly previewWriter: WritableStreamDefaultWriter<VideoFrame> | null;

    private disposed = false;
    private sweepTimer: ReturnType<typeof setInterval> | null = null;

    constructor(opts: SenderSessionOptions = {}) {
        const createCaptureClock = opts.createCaptureClock
            ?? (() => new MonotonicClock({ minTickMs: 33 }));
        this.captureClock = createCaptureClock();
        this.encoderPool = new EncoderPool(opts.encoderPoolOptions);
        this.previewWriter = acquirePreviewWriter(opts.previewGenerator);
        // Periodic sweep so a long-idle session releases parked encoders even
        // when no further acquire() runs the in-acquire sweep.
        this.sweepTimer = setInterval(() => {
            try { this.encoderPool.sweep(); } catch { /* ignore */ }
        }, PoolSweepIntervalMs);
    }

    get isDisposed(): boolean { return this.disposed; }

    // No-op today. Stable extension point for cross-run resets; encoderPool
    // is intentionally NOT touched — surviving between runs is its purpose.
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    reset(): void {
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        if (this.sweepTimer !== null) {
            try { clearInterval(this.sweepTimer); } catch { /* ignore */ }
            this.sweepTimer = null;
        }
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
        return null;
    }
}

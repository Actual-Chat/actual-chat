// Sender-side session: long-lived resources reused across recording runs.
// One instance per worker lifetime. Per-run state lives in operator closures;
// what survives across runs (capture-clock monotonicity, preview-track writer)
// lives here. Encoders are intentionally NOT pooled — every run gets a fresh
// `VideoEncoder` so its first encoded chunk is guaranteed to be a keyframe.

import { MonotonicClock } from 'clocks';

export interface PreviewGeneratorLike {
    writable: WritableStream<VideoFrame>;
}

export interface SenderSessionOptions {
    previewGenerator?: PreviewGeneratorLike;
    createCaptureClock?: () => MonotonicClock;
}

export class SenderSession {
    readonly captureClock: MonotonicClock;
    readonly previewWriter: WritableStreamDefaultWriter<VideoFrame> | null;

    private disposed = false;

    constructor(opts: SenderSessionOptions = {}) {
        const createCaptureClock = opts.createCaptureClock
            ?? (() => new MonotonicClock({ minTickMs: 33 }));
        this.captureClock = createCaptureClock();
        this.previewWriter = acquirePreviewWriter(opts.previewGenerator);
    }

    get isDisposed(): boolean { return this.disposed; }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
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

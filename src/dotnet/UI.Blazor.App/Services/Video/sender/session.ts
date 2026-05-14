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
    private previewWriter: WritableStreamDefaultWriter<VideoFrame> | null = null;

    private disposed = false;

    constructor(opts: SenderSessionOptions = {}) {
        const createCaptureClock = opts.createCaptureClock
            ?? (() => new MonotonicClock({ minTickMs: 33 }));
        this.captureClock = createCaptureClock();
        this.setPreviewGenerator(opts.previewGenerator);
    }

    get isDisposed(): boolean { return this.disposed; }
    getPreviewWriter(): WritableStreamDefaultWriter<VideoFrame> | null { return this.previewWriter; }

    setPreviewGenerator(generator: PreviewGeneratorLike | undefined): void {
        this.releasePreviewWriter();
        this.previewWriter = acquirePreviewWriter(generator);
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.releasePreviewWriter();
    }

    private releasePreviewWriter(): void {
        if (!this.previewWriter) return;
        try { this.previewWriter.releaseLock(); } catch { /* ignore */ }
        this.previewWriter = null;
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

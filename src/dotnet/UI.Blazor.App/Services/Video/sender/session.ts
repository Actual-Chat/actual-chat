// Sender-side session: long-lived resources reused across recording runs.
// One instance per worker lifetime. Per-run state lives in operator closures;
// what survives across runs (capture-clock monotonicity, preview-track writer,
// encoder pool) lives here. The encoder pool parks released encoders for a
// short TTL so reuse across rapid restarts doesn't churn the platform's
// HW encoder slot (e.g. NVENC concurrent-session limit). Keyframe guarantees
// on first encode come from explicit `keyFrame: true` requests + a runtime
// verification of `chunk.type` in the encode operator, not from "fresh encoder".

import { MonotonicClock } from 'clocks';
import { EncoderPool } from './encoder-pool';
import type { PreviewFramePresentation, PreviewTrace } from './recorder-worker-contract';

export interface PreviewGeneratorLike {
    writable: WritableStream<VideoFrame>;
}

export interface SenderSessionOptions {
    previewGenerator?: PreviewGeneratorLike;
    onPreviewFrame?: (frame: VideoFrame) => void | Promise<void>;
    onPreviewFramePresentation?: (presentation: PreviewFramePresentation) => void;
    createCaptureClock?: () => MonotonicClock;
}

export function createEmptyPreviewTrace(): PreviewTrace {
    return {
        forwarded: 0, noConsumer: 0, refused: 0, cloneFailed: 0,
        writeCalled: 0, writeResolved: 0, writeRejected: 0, reported: 0,
        lastForwardedAtMs: 0, lastWriteResolvedAtMs: 0,
        lastDesiredSize: null, lastError: '',
    };
}

export class SenderSession {
    readonly captureClock: MonotonicClock;
    readonly encoderPool: EncoderPool;
    // Survives runs so a restart's trace is comparable with the run before it.
    readonly previewTrace: PreviewTrace = createEmptyPreviewTrace();
    private previewWriter: WritableStreamDefaultWriter<VideoFrame> | null = null;
    private onPreviewFrame: ((frame: VideoFrame) => void | Promise<void>) | null = null;
    private onPreviewFramePresentation: ((presentation: PreviewFramePresentation) => void) | null = null;

    private disposed = false;
    private lastPreviewFramePresentation: PreviewFramePresentation | null = null;

    constructor(opts: SenderSessionOptions = {}) {
        const createCaptureClock = opts.createCaptureClock
            ?? (() => new MonotonicClock({ minTickMs: 33 }));
        this.captureClock = createCaptureClock();
        this.encoderPool = new EncoderPool();
        this.onPreviewFrame = opts.onPreviewFrame ?? null;
        this.onPreviewFramePresentation = opts.onPreviewFramePresentation ?? null;
        this.setPreviewGenerator(opts.previewGenerator);
    }

    get isDisposed(): boolean { return this.disposed; }
    getPreviewWriter(): WritableStreamDefaultWriter<VideoFrame> | null { return this.previewWriter; }
    reportPreviewFrame(frame: VideoFrame): void | Promise<void> {
        return this.onPreviewFrame?.(frame);
    }
    reportPreviewFramePresentation(presentation: PreviewFramePresentation): void {
        const last = this.lastPreviewFramePresentation;
        if (last?.rotation === presentation.rotation)
            return;

        this.lastPreviewFramePresentation = presentation;
        this.onPreviewFramePresentation?.(presentation);
    }

    setPreviewGenerator(generator: PreviewGeneratorLike | undefined): void {
        this.releasePreviewWriter();
        this.lastPreviewFramePresentation = null;
        this.previewWriter = acquirePreviewWriter(generator);
    }

    setPreviewFramePresentationReporter(
        reporter: ((presentation: PreviewFramePresentation) => void) | undefined,
    ): void {
        this.onPreviewFramePresentation = reporter ?? null;
    }

    setPreviewFrameReporter(
        reporter: ((frame: VideoFrame) => void | Promise<void>) | undefined,
    ): void {
        this.onPreviewFrame = reporter ?? null;
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.releasePreviewWriter();
        try { this.encoderPool.dispose(); } catch { /* ignore */ }
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

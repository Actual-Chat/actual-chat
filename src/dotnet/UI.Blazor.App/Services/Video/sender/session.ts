// Sender-side session: long-lived resources reused across recording runs.
// One instance per worker lifetime. Per-run state lives in operator closures;
// what survives across runs (capture-clock monotonicity, preview-track writer)
// lives here. Encoders are intentionally NOT pooled — every run gets a fresh
// `VideoEncoder` so its first encoded chunk is guaranteed to be a keyframe.

import { MonotonicClock } from 'clocks';
import type { PreviewFramePresentation } from './recorder-worker-contract';

export interface PreviewGeneratorLike {
    writable: WritableStream<VideoFrame>;
}

export interface SenderSessionOptions {
    previewGenerator?: PreviewGeneratorLike;
    onPreviewFrame?: (frame: VideoFrame) => void | Promise<void>;
    onPreviewFramePresentation?: (presentation: PreviewFramePresentation) => void;
    createCaptureClock?: () => MonotonicClock;
}

export class SenderSession {
    readonly captureClock: MonotonicClock;
    private previewWriter: WritableStreamDefaultWriter<VideoFrame> | null = null;
    private onPreviewFrame: ((frame: VideoFrame) => void | Promise<void>) | null = null;
    private onPreviewFramePresentation: ((presentation: PreviewFramePresentation) => void) | null = null;

    private disposed = false;
    private lastPreviewFramePresentation: PreviewFramePresentation | null = null;

    constructor(opts: SenderSessionOptions = {}) {
        const createCaptureClock = opts.createCaptureClock
            ?? (() => new MonotonicClock({ minTickMs: 33 }));
        this.captureClock = createCaptureClock();
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

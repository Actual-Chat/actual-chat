/**
 * BlurPreviewSession
 * Standalone background-blur preview worker.
 *
 * Camera track is cloned and transferred to the worker; the worker reads frames
 * via MediaStreamTrackProcessor, blurs them, and writes the result to a
 * MediaStreamTrackGenerator. The output `MediaStreamTrack` is delivered back to
 * main and exposed via {@link previewTrack} so the caller can attach it to a
 * `<video srcObject>`. Requires worker-scope MSTP + MSTG (Chromium and Safari 18+).
 */

import { getLogs } from 'logging';
import { Api } from 'api';
import { rpcClientServer } from 'rpc';
import type { Disposable } from 'disposable';
import type { VideoProcessingWorker, VideoProcessingWorkerCallbacks } from '../workers/video-processing-worker-contract';
import { createAdaptiveSegmentationConfig } from '../workers/video-processing-worker-contract';
import { detectGPUBackends } from '../gpu-support';
import { Versioning } from 'versioning';

const { infoLog } = getLogs('BlurPreviewSession');

export interface BlurPreviewSessionOptions {
    /** Live camera track. The session clones and transfers the clone to the
     *  worker; the original stays with the caller. */
    track: MediaStreamTrack;
}

export class BlurPreviewSession {
    private readonly workerInstance: Worker;
    private readonly worker: VideoProcessingWorker & Disposable;
    private readonly _previewTrack: MediaStreamTrack;
    private _isActive = false;

    /** Blurred-output `MediaStreamTrack`. Caller attaches it to a `<video srcObject>`. */
    get previewTrack(): MediaStreamTrack { return this._previewTrack; }
    get isActive(): boolean { return this._isActive; }

    private constructor(
        workerInstance: Worker,
        worker: VideoProcessingWorker & Disposable,
        previewTrack: MediaStreamTrack,
    ) {
        this.workerInstance = workerInstance;
        this.worker = worker;
        this._previewTrack = previewTrack;
    }

    static async create(options: BlurPreviewSessionOptions): Promise<BlurPreviewSession> {
        const gpuSupport = await detectGPUBackends();
        const segConfig = createAdaptiveSegmentationConfig(gpuSupport.recommended);

        const workerPath = Versioning.mapPath('/dist/videoProcessingWorker.js');
        const workerInstance = new Worker(workerPath, { type: 'module' });

        let resolvePreviewTrack: ((track: MediaStreamTrack) => void) | null = null;
        const previewTrackPromise = new Promise<MediaStreamTrack>(resolve => {
            resolvePreviewTrack = resolve;
        });

        const worker = rpcClientServer<VideoProcessingWorker>(
            'PreviewBlur',
            workerInstance,
            {
                // Worker invokes this only if MSTG setup fails inside the worker
                // scope (`previewWriter` null branch in video-processing.ts).
                // We require MSTG to succeed, so this is effectively a no-op
                // safety stub — drop the frame to avoid leaking VideoFrames.
                onPreviewFrame: (frame: VideoFrame) => {
                    frame.close();
                    return Promise.resolve();
                },
                onPreviewTrack: (track: MediaStreamTrack) => {
                    resolvePreviewTrack?.(track);
                    resolvePreviewTrack = null;
                    return Promise.resolve();
                },
                onSerializedChunk: () => Promise.resolve(),
                onBackpressure: () => Promise.resolve(),
                onEncoderFailed: () => Promise.resolve(),
                onStreamingStalled: () => Promise.resolve(),
                onDimensionReconciled: () => Promise.resolve(),
                onStreamCreated: () => Promise.resolve(),
                getSessionToken: (minLifespanMs?: number) => Api.getSessionToken(minLifespanMs),
            } as VideoProcessingWorkerCallbacks,
        );

        // Encoder block is required by the contract but unused in preview-only mode.
        const dummyEncoderConfig = {
            codec: 'av01.0.01M.08', width: 640, height: 360, bitrate: 500_000,
            framerate: 15, keyframeInterval: 30, latencyMode: 'realtime' as const,
            hardwareAcceleration: 'prefer-software' as const,
        };
        const config = {
            encoder: dummyEncoderConfig,
            segmentation: segConfig,
            streaming: { apiUrl: '', chatId: '', serverClockOffsetMs: 0 },
            previewOnly: true,
        };

        try {
            const trackClone = options.track.clone();
            await worker.startPreviewWithTrack(config, trackClone, { type: 'rpc-timeout', timeoutMs: 15000 });
            const previewTrack = await previewTrackPromise;
            const session = new BlurPreviewSession(workerInstance, worker, previewTrack);
            session._isActive = true;
            infoLog?.log('Blur preview session started');
            return session;
        } catch (error) {
            worker.dispose();
            workerInstance.terminate();
            throw error;
        }
    }

    /** Stop the session and release all resources. Idempotent. */
    async stop(): Promise<void> {
        if (!this._isActive) return;
        this._isActive = false;

        // Stop MSTG output track explicitly so any consumer's <video srcObject>
        // sees an ended track immediately, instead of waiting for worker
        // termination to propagate via GC.
        try { this._previewTrack.stop(); } catch { /* ignore */ }

        try {
            await this.worker.stop();
            this.worker.dispose();
        } catch {
            // ignore cleanup errors
        }

        this.workerInstance.terminate();
        infoLog?.log('Blur preview session stopped');
    }
}

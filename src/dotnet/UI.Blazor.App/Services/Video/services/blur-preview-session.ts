/**
 * BlurPreviewSession
 * Encapsulates a standalone background-blur preview worker.
 * Creates a VideoProcessingWorker in previewOnly mode, pumps camera frames
 * from a source video element, and renders blurred output to a target canvas.
 */

import { getLogs } from 'logging';
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import type { VideoProcessingWorker, VideoProcessingWorkerCallbacks } from '../workers/video-processing-worker-contract';
import { createAdaptiveSegmentationConfig } from '../workers/video-processing-worker-contract';
import { detectGPUBackends } from '../gpu-support';
import { Versioning } from 'versioning';
import { CanvasTarget } from './canvas-target';

const { infoLog } = getLogs('BlurPreviewSession');

export interface BlurPreviewSessionOptions {
    /** Video element to capture frames from. */
    source: HTMLVideoElement;
    /** Canvas to render blurred output to. */
    target: HTMLCanvasElement;
    /** Target FPS for the frame pump (default: 15). */
    fps?: number;
}

export class BlurPreviewSession {
    private readonly source: HTMLVideoElement;
    private readonly captureTarget: CanvasTarget;
    private readonly workerInstance: Worker;
    private readonly worker: VideoProcessingWorker & Disposable;
    private readonly pumpIntervalMs: number;
    private pumpTimer: number | null = null;
    private _isActive = false;

    get isActive(): boolean { return this._isActive; }

    private constructor(
        options: BlurPreviewSessionOptions,
        workerInstance: Worker,
        worker: VideoProcessingWorker & Disposable,
    ) {
        this.source = options.source;
        this.captureTarget = new CanvasTarget(document.createElement('canvas'));
        this.workerInstance = workerInstance;
        this.worker = worker;
        this.pumpIntervalMs = Math.round(1000 / (options.fps ?? 15));
    }

    /**
     * Create and start a blur preview session.
     * Detects GPU backend, initializes the worker in previewOnly mode, and starts pumping frames.
     */
    static async create(options: BlurPreviewSessionOptions): Promise<BlurPreviewSession> {
        const gpuSupport = await detectGPUBackends();
        const segConfig = createAdaptiveSegmentationConfig(gpuSupport.recommended);

        const workerPath = Versioning.mapPath('/dist/videoProcessingWorker.js');
        const workerInstance = new Worker(workerPath, { type: 'module' });

        const target = new CanvasTarget(options.target);
        const worker = rpcClientServer<VideoProcessingWorker>(
            'PreviewBlur',
            workerInstance,
            {
                onPreviewFrame: (frame: VideoFrame) => {
                    target.draw(frame, frame.displayWidth, frame.displayHeight);
                    frame.close();
                    return Promise.resolve();
                },
                onSerializedChunk: () => Promise.resolve(),
                onBackpressure: () => Promise.resolve(),
                onEncoderFailed: () => Promise.resolve(),
                onDimensionReconciled: () => Promise.resolve(),
                onStreamCreated: () => Promise.resolve(),
            } as VideoProcessingWorkerCallbacks,
        );

        await worker.initialize({
            encoder: {
                codec: 'av01.0.01M.08', width: 640, height: 360, bitrate: 500_000,
                framerate: 15, keyframeInterval: 30, latencyMode: 'realtime',
                hardwareAcceleration: 'prefer-software'
            },
            segmentation: segConfig,
            streaming: { apiUrl: '', sessionToken: '', chatId: '', serverClockOffsetMs: 0 },
            previewOnly: true,
        }, { type: 'rpc-timeout', timeoutMs: 15000 });

        const session = new BlurPreviewSession(options, workerInstance, worker);
        session._isActive = true;
        session.pumpFrames();
        infoLog?.log('Blur preview session started');
        return session;
    }

    /** Stop the session and release all resources. Idempotent. */
    async stop(): Promise<void> {
        if (!this._isActive) return;
        this._isActive = false;

        if (this.pumpTimer !== null) {
            clearTimeout(this.pumpTimer);
            this.pumpTimer = null;
        }

        try {
            await this.worker.stop();
            this.worker.dispose();
        } catch {
            // ignore cleanup errors
        }

        this.workerInstance.terminate();
        infoLog?.log('Blur preview session stopped');
    }

    private pumpFrames(): void {
        if (!this._isActive) return;

        if (this.source.videoWidth > 0 && this.source.videoHeight > 0) {
            this.captureTarget.draw(this.source, this.source.videoWidth, this.source.videoHeight);

            const frame = new VideoFrame(this.captureTarget.element, {
                timestamp: performance.now() * 1000,
            });

            void this.worker.encodeFrame(frame, rpcNoWait);
        }

        this.pumpTimer = window.setTimeout(() => this.pumpFrames(), this.pumpIntervalMs);
    }
}

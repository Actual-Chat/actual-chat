/**
 * BlurPreviewSession
 * Encapsulates a standalone background-blur preview worker. Two output modes:
 *
 *  - MSTG path (preferred): camera track is cloned and transferred to the worker;
 *    the worker reads frames via MediaStreamTrackProcessor, blurs them, and
 *    writes the result to a MediaStreamTrackGenerator. The output `MediaStreamTrack`
 *    is delivered back to main and exposed via {@link previewTrack} so the caller
 *    can attach it to a `<video srcObject>`. No per-frame RPC.
 *
 *  - Pump fallback (legacy): main pumps frames from a `<video>` source via
 *    setTimeout + drawImage, posts each `VideoFrame` to the worker over RPC,
 *    and draws the blurred frame returned by `onPreviewFrame` into the caller's
 *    `<canvas>`. Used when the worker scope lacks MSTP/MSTG (older Safari).
 */

import { getLogs } from 'logging';
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import type { VideoProcessingWorker, VideoProcessingWorkerCallbacks } from '../workers/video-processing-worker-contract';
import { createAdaptiveSegmentationConfig } from '../workers/video-processing-worker-contract';
import { detectGPUBackends } from '../gpu-support';
import { Versioning } from 'versioning';
import { CanvasTarget } from './canvas-target';

const { infoLog, warnLog } = getLogs('BlurPreviewSession');

export interface BlurPreviewSessionOptions {
    /** Live camera track. The session clones and transfers the clone to the
     *  worker for the MSTG path; the original stays with the caller. */
    track: MediaStreamTrack;
    /** Frame source for the pump fallback. Required to enable the fallback path. */
    source?: HTMLVideoElement;
    /** Canvas target for the pump fallback. Required to enable the fallback path. */
    target?: HTMLCanvasElement;
    /** Target FPS for the pump fallback (default: 15). Ignored in MSTG mode —
     *  there the worker reads frames at the camera's native rate via MSTP. */
    fps?: number;
}

export type BlurPreviewMode = 'mstg' | 'pump';

export class BlurPreviewSession {
    private readonly mode: BlurPreviewMode;
    private readonly workerInstance: Worker;
    private readonly worker: VideoProcessingWorker & Disposable;
    private readonly _previewTrack: MediaStreamTrack | null;
    /** Pump-mode only — null in MSTG mode. */
    private readonly source: HTMLVideoElement | null;
    private readonly captureTarget: CanvasTarget | null;
    private readonly pumpIntervalMs: number;
    private pumpTimer: number | null = null;
    private _isActive = false;

    /** When set: the blurred-output `MediaStreamTrack` (MSTG path). The caller
     *  attaches it to a `<video srcObject>`. Null in pump mode (caller relies on
     *  the canvas target instead). */
    get previewTrack(): MediaStreamTrack | null { return this._previewTrack; }
    get isActive(): boolean { return this._isActive; }
    get currentMode(): BlurPreviewMode { return this.mode; }

    private constructor(
        mode: BlurPreviewMode,
        workerInstance: Worker,
        worker: VideoProcessingWorker & Disposable,
        previewTrack: MediaStreamTrack | null,
        source: HTMLVideoElement | null,
        target: HTMLCanvasElement | null,
        fps: number,
    ) {
        this.mode = mode;
        this.workerInstance = workerInstance;
        this.worker = worker;
        this._previewTrack = previewTrack;
        this.source = source;
        this.captureTarget = target ? new CanvasTarget(document.createElement('canvas')) : null;
        if (target && !source) warnLog?.log('Pump fallback set up without a source — pump will be inert');
        this.pumpIntervalMs = Math.round(1000 / fps);
    }

    /**
     * Create and start a blur preview session. Tries the MSTG path first; falls
     * back to the pump path if the worker reports MSTG/MSTP unavailable. The
     * caller MUST provide `source` + `target` to enable the fallback — otherwise
     * a worker without MSTG raises an error.
     */
    static async create(options: BlurPreviewSessionOptions): Promise<BlurPreviewSession> {
        const gpuSupport = await detectGPUBackends();
        const segConfig = createAdaptiveSegmentationConfig(gpuSupport.recommended);

        const workerPath = Versioning.mapPath('/dist/videoProcessingWorker.js');
        const workerInstance = new Worker(workerPath, { type: 'module' });

        // The MSTG path delivers the output track via `onPreviewTrack`. We hold a
        // resolver here so `create()` can await the track before returning. The
        // pump path uses `onPreviewFrame` per-frame instead.
        let resolvePreviewTrack: ((track: MediaStreamTrack) => void) | null = null;
        const previewTrackPromise = new Promise<MediaStreamTrack>(resolve => {
            resolvePreviewTrack = resolve;
        });

        const target = options.target ? new CanvasTarget(options.target) : null;

        const worker = rpcClientServer<VideoProcessingWorker>(
            'PreviewBlur',
            workerInstance,
            {
                onPreviewFrame: (frame: VideoFrame) => {
                    if (target) {
                        target.draw(frame, frame.displayWidth, frame.displayHeight);
                    }
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
                onDimensionReconciled: () => Promise.resolve(),
                onStreamCreated: () => Promise.resolve(),
            } as VideoProcessingWorkerCallbacks,
        );

        const fps = options.fps ?? 15;
        // Encoder block is required by the contract but unused in preview-only mode.
        const dummyEncoderConfig = {
            codec: 'av01.0.01M.08', width: 640, height: 360, bitrate: 500_000,
            framerate: fps, keyframeInterval: 30, latencyMode: 'realtime' as const,
            hardwareAcceleration: 'prefer-software' as const,
        };
        const config = {
            encoder: dummyEncoderConfig,
            segmentation: segConfig,
            streaming: { apiUrl: '', sessionToken: '', chatId: '', serverClockOffsetMs: 0 },
            previewOnly: true,
        };

        // Attempt MSTG path. The worker throws if MSTP or MSTG is unavailable in
        // worker scope — we then fall back to the legacy pump.
        try {
            const trackClone = options.track.clone();
            await worker.startPreviewWithTrack(config, trackClone, { type: 'rpc-timeout', timeoutMs: 15000 });
            const previewTrack = await previewTrackPromise;
            const session = new BlurPreviewSession(
                'mstg', workerInstance, worker, previewTrack, null, null, fps,
            );
            session._isActive = true;
            infoLog?.log('Blur preview session started (MSTG mode)');
            return session;
        } catch (mstgError) {
            warnLog?.log('MSTG path unavailable, falling back to pump mode:', mstgError);

            if (!options.source || !options.target) {
                worker.dispose();
                workerInstance.terminate();
                throw new Error(
                    'BlurPreviewSession: MSTG unavailable and no pump-fallback source/target provided',
                );
            }

            await worker.initialize(config, { type: 'rpc-timeout', timeoutMs: 15000 });

            const session = new BlurPreviewSession(
                'pump', workerInstance, worker, null, options.source, options.target, fps,
            );
            session._isActive = true;
            session.pumpFrames();
            infoLog?.log('Blur preview session started (pump mode)');
            return session;
        }
    }

    /** Stop the session and release all resources. Idempotent. */
    async stop(): Promise<void> {
        if (!this._isActive) return;
        this._isActive = false;

        if (this.pumpTimer !== null) {
            clearTimeout(this.pumpTimer);
            this.pumpTimer = null;
        }

        // Stop MSTG output track explicitly so any consumer's <video srcObject>
        // sees an ended track immediately, instead of waiting for worker
        // termination to propagate via GC.
        if (this._previewTrack) {
            try { this._previewTrack.stop(); } catch { /* ignore */ }
        }

        try {
            await this.worker.stop();
            this.worker.dispose();
        } catch {
            // ignore cleanup errors
        }

        this.workerInstance.terminate();
        infoLog?.log(`Blur preview session stopped (${this.mode} mode)`);
    }

    private pumpFrames(): void {
        if (!this._isActive || !this.source || !this.captureTarget) return;

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

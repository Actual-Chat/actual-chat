/**
 * Video Pipeline
 * Thin orchestrator that creates a unified Video Processing Worker and transfers
 * the camera ReadableStream to it. All encoding, segmentation, and Fusion RPC
 * streaming happen inside the worker — the main thread only handles:
 *   - MSTP/canvas frame extraction
 *   - VAD state forwarding
 *   - Preview frame rendering
 *   - Recording UI lifecycle
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import { supportsTransferableStreams } from '../workers/stream-channel';

import { BrowserInit } from '../../../../UI.Blazor/Services/BrowserInit/browser-init';
import { ConnectivityUI } from '../../../../UI.Blazor/Services/ConnectivityUI/connectivity-ui';
import { Api, WorkerKind } from 'api';
import type { EncoderConfig, EncoderStats } from '../webcodecs-encoder';
import type { SegmentationConfig, SegmentationStats, OrientationStats } from '../workers/video-processing-worker-contract';
import type {
    VideoProcessingWorker,
    VideoProcessingWorkerCallbacks,
    VideoProcessingConfig,
    VideoProcessingStats,
} from '../workers/video-processing-worker-contract';
import { Versioning } from 'versioning';
import { getLogs } from 'logging';
import { SessionTokens } from '../../../../UI.Blazor/Services/Security/session-tokens';
import { ServerClock } from 'server-clock';
import type { Subscription } from 'rxjs';
import { RecorderStateHub } from '../../../Components/AudioRecorder/recorder-state-hub';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

export interface PipelineConfig {
    encoderConfig: EncoderConfig;
    backgroundBlur?: {
        enabled: boolean;
        segmentationConfig: SegmentationConfig;
    };
    frameDropping?: {
        enabled: boolean;
        dropProbability?: number;
    };
    streaming?: {
        enabled: boolean;
        chatId: string;
        streamKind?: number; // 0 = Webcam (default), 1 = Screencast
    };
    adaptiveFramerate?: {
        enabled: boolean;
        reducedFps?: number;
        reducedBitrateRatio?: number;
        silenceDelayMs?: number;
    };
}

// Type declarations for Insertable Streams API
declare class MediaStreamTrackProcessor<T = VideoFrame> {
    constructor(options: { track: MediaStreamTrack });
    readable: ReadableStream<T>;
}

export interface IVideoPipeline {
    start(inputStream: MediaStream): Promise<void>;
    stop(): Promise<void>;
    reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void>;
    switchCodec(newCodecString: string): Promise<void>;
    toggleBlur(enabled: boolean, segmentationConfig?: SegmentationConfig): Promise<void>;
    switchSegmentationBackend(backend: 'webgpu' | 'wasm'): Promise<void>;
    setPreviewCallback(callback: ((frame: VideoFrame) => void) | null): void;
    getEncoderStats(): EncoderStats;
    getSegmentationStats(): SegmentationStats | null;
    getOrientationStats(): OrientationStats | null;
    pauseEncoding(): void;
    resumeEncoding(): void;
    forceKeyFrame(): Promise<void>;
}

export class VideoPipeline implements IVideoPipeline {
    private readonly workerInstance: Worker;
    private readonly worker: (VideoProcessingWorker & Disposable);
    private readonly useStreams: boolean;
    private readonly useTrackTransfer: boolean;
    private processor: MediaStreamTrackProcessor | null = null;
    private frameReader: ReadableStreamDefaultReader<VideoFrame> | null = null;

    // Preview callback for rendering blurred frames on main thread
    private previewCallback: ((frame: VideoFrame) => void) | null = null;

    // Common
    private processing = false;

    // VAD-based adaptive framerate (main thread tracks state, forwards to worker)
    private remoteStreamCount = 0;
    private isSpeaking = true;
    private vadSubscription: Subscription | null = null;
    private vadSilenceTimer: ReturnType<typeof setTimeout> | null = null;
    private savedBitrate: number;
    private serverPaused = false;

    // Encoder backpressure state
    private backpressureStepDownCount = 0;
    private lastBackpressureStepDown = 0;

    // Encoder failure callback (set by recording-service for codec fallback)
    public onEncoderFailure: ((failedCodec: string) => void) | null = null;

    // Server clock sync
    private clockUnsubscribe: (() => void) | null = null;

    // Disconnect-api handler — removed from the event set on stop().
    private _disconnectApiHandler: (() => void) | null = null;

    // Stats (polled from worker)
    private currentStats: VideoProcessingStats = {
        encoder: {
            encodedFrames: 0, droppedFrames: 0, keyFrames: 0, totalBytes: 0,
            averageEncodeTime: 0, medianEncodeTime: 0, pureMedianEncodeTime: -1,
            configuredWidth: 0, configuredHeight: 0, configuredBitrate: 0,
            hardwareAcceleration: 'unknown',
        },
        segmentation: null,
        orientation: null,
    };
    private statsInterval: number | null = null;
    private diagnosticsInterval: number | null = null;
    private lastDiagTotalBytes = 0;
    private lastDiagEncodedFrames = 0;

    constructor(private config: PipelineConfig) {
        this.savedBitrate = config.encoderConfig.bitrate;

        // Detect best frame delivery mode:
        // 1. Transferable streams (Chrome) — transfer ReadableStream<VideoFrame> to worker
        // 2. Track transfer (Safari 18+) — transfer MediaStreamTrack, worker creates MSTP
        // 3. RPC fallback — per-frame postMessage with VideoFrame transfer
        this.useStreams = supportsTransferableStreams();
        this.useTrackTransfer = !this.useStreams && this.supportsTrackTransfer();
        const mode = this.useStreams ? 'stream' : this.useTrackTransfer ? 'track transfer' : 'RPC fallback';
        infoLog?.log(`Frame delivery mode: ${mode}`);

        // Create unified video processing worker
        const workerPath = Versioning.mapPath('/dist/videoProcessingWorker.js');
        infoLog?.log('Creating video processing worker from:', workerPath);
        this.workerInstance = new Worker(workerPath, { type: 'module' });
        this.workerInstance.onerror = (e) => errorLog?.log('Video processing worker error:', e);

        // Create RPC proxy with callbacks
        this.worker = rpcClientServer<VideoProcessingWorker>(
            'VideoPipeline.worker',
            this.workerInstance,
            {
                onSerializedChunk: () => {
                    // RPC fallback only — not used in stream mode
                    return Promise.resolve();
                },
                onBackpressure: (dropRate: number) => {
                    this.handleEncoderBackpressure(dropRate);
                    return Promise.resolve();
                },
                onEncoderFailed: (codec: string) => {
                    this.handleEncoderFailure(codec);
                    return Promise.resolve();
                },
                onDimensionReconciled: (width: number, height: number) => {
                    infoLog?.log(`Dimension reconciled by worker: ${width}x${height}`);
                    this.config.encoderConfig.width = width;
                    this.config.encoderConfig.height = height;
                    return Promise.resolve();
                },
                onPreviewFrame: (frame: VideoFrame) => {
                    if (this.previewCallback) {
                        try {
                            this.previewCallback(frame);
                        } catch (error) {
                            errorLog?.log('Preview callback error:', error);
                        }
                    }
                    frame.close();
                    return Promise.resolve();
                },
                onStreamCreated: (codecSettings: string) => {
                    infoLog?.log(`Worker created RPC stream, codecSettings: ${codecSettings.length} chars`);
                    return Promise.resolve();
                },
            } as VideoProcessingWorkerCallbacks,
        );

        // Mirror `ConnectivityUI` → worker's `WorkerConnectivityUI` → Api
        // so the worker's peer honors `isDotNetRpcConnected`. Same pattern as
        // `opus-media-recorder` uses for the audio worker.
        const pushConnectivity = (): void => {
            void this.worker.onConnectivityUpdate(
                ConnectivityUI.isOnline,
                ConnectivityUI.isConnected,
                ConnectivityUI.isBlazorServer,
                rpcNoWait);
        };
        ConnectivityUI.isOnlineChanged.add(pushConnectivity);
        ConnectivityUI.isConnectedChanged.add(pushConnectivity);
        void ConnectivityUI.whenReady.then(pushConnectivity);

        this._disconnectApiHandler = () => void this.worker.disconnectApi(rpcNoWait);
        Api.onDisconnectRequested(WorkerKind.VideoCapture).add(this._disconnectApiHandler);
    }

    public async start(inputStream: MediaStream): Promise<void> {
        infoLog?.log('Starting video pipeline...');

        const videoTrack = inputStream.getVideoTracks()[0];
        this.processing = true;

        // Build worker config
        // Fusion RPC WebSocket URL — worker pushes frames via
        // `IStreamServer.PushVideo` over this connection.
        const apiUrl = BrowserInit.getUrl('/rpc/ws').replace(/^http/, 'ws');
        const workerConfig: VideoProcessingConfig = {
            encoder: this.config.encoderConfig,
            streaming: {
                apiUrl,
                sessionToken: SessionTokens.current,
                chatId: this.config.streaming?.chatId ?? '',
                serverClockOffsetMs: ServerClock.offsetMs,
                streamKind: this.config.streaming?.streamKind ?? 0,
            },
        };

        if (this.config.backgroundBlur?.enabled) {
            workerConfig.segmentation = this.config.backgroundBlur.segmentationConfig;
        }

        if (this.config.adaptiveFramerate?.enabled) {
            workerConfig.adaptiveFramerate = {
                reducedFps: this.config.adaptiveFramerate.reducedFps ?? 5,
            };
        }

        if (this.useStreams) {
            await this.startStreamMode(videoTrack, workerConfig);
        } else if (this.useTrackTransfer) {
            await this.startTrackTransferMode(videoTrack, workerConfig);
        } else {
            await this.startRpcFallbackMode(videoTrack, workerConfig);
        }

        // Subscribe to server clock offset changes
        this.clockUnsubscribe = ServerClock.onOffsetChanged((offsetMs) => {
            void this.worker.updateServerClockOffset(offsetMs);
        });

        // Start stats polling
        this.statsInterval = window.setInterval(() => {
            void (async () => {
                this.currentStats = await this.worker.getStats();
            })();
        }, 1000);

        // Start 10s diagnostics timer
        this.lastDiagTotalBytes = 0;
        this.lastDiagEncodedFrames = 0;
        this.diagnosticsInterval = window.setInterval(() => {
            const s = this.currentStats.encoder;
            const bytesDelta = s.totalBytes - this.lastDiagTotalBytes;
            const framesDelta = s.encodedFrames - this.lastDiagEncodedFrames;
            this.lastDiagTotalBytes = s.totalBytes;
            this.lastDiagEncodedFrames = s.encodedFrames;

            const actualMbps = (bytesDelta * 8 / 10_000_000).toFixed(2);
            const cfgMbps = (s.configuredBitrate / 1_000_000).toFixed(1);
            const codec = this.config.encoderConfig.codec;
            warnLog?.log(
                `VIDEO_ENCODE: codec=${codec} median=${s.medianEncodeTime.toFixed(1)}ms avg=${s.averageEncodeTime.toFixed(1)}ms ` +
                `cfg=${s.configuredWidth}x${s.configuredHeight}@${cfgMbps}Mbps actual=${actualMbps}Mbps ` +
                `enc=${framesDelta} drop=${s.droppedFrames} kf=${s.keyFrames} hw=${s.hardwareAcceleration}`);

            const seg = this.currentStats.segmentation;
            if (seg) {
                warnLog?.log(
                    `VIDEO_SEG: infer=${seg.averageInferenceTime.toFixed(1)}ms blur=${seg.averageBlurTime.toFixed(1)}ms ` +
                    `total=${seg.averageTotalTime.toFixed(1)}ms drop=${seg.droppedFrames} backend=${seg.backend}`);
            }
        }, 10_000);

        infoLog?.log('Pipeline started');
    }

    /**
     * Stream mode: transfer MSTP ReadableStream to worker. Worker handles everything.
     */
    private async startStreamMode(videoTrack: MediaStreamTrack, config: VideoProcessingConfig): Promise<void> {
        infoLog?.log('Starting stream mode...');

        let frameReadable: ReadableStream<VideoFrame>;
        if (this.hasMSTPInWindow()) {
            try {
                this.processor = new MediaStreamTrackProcessor({ track: videoTrack });
                frameReadable = this.processor.readable;
                debugLog?.log('Using MSTP ReadableStream');
            } catch (error) {
                errorLog?.log('MSTP creation failed, falling back to canvas:', error);
                frameReadable = this.createCanvasReadableStream(videoTrack);
            }
        } else {
            frameReadable = this.createCanvasReadableStream(videoTrack);
        }

        await this.worker.startWithStream(config, frameReadable, { type: 'rpc-timeout', timeoutMs: 15000 });
        infoLog?.log('Stream mode started');
    }

    /**
     * Track transfer mode: transfer MediaStreamTrack to worker, worker creates MSTP.
     * Used on Safari 18+ where ReadableStream isn't transferable but MediaStreamTrack is.
     */
    private async startTrackTransferMode(videoTrack: MediaStreamTrack, config: VideoProcessingConfig): Promise<void> {
        infoLog?.log('Starting track transfer mode...');
        try {
            await this.worker.startWithTrack(config, videoTrack, { type: 'rpc-timeout', timeoutMs: 15000 });
            infoLog?.log('Track transfer mode started');
        } catch (error) {
            warnLog?.log('Track transfer mode failed, falling back to RPC:', error);
            await this.startRpcFallbackMode(videoTrack, config);
        }
    }

    /**
     * RPC fallback: pump frames via per-frame RPC calls.
     */
    private async startRpcFallbackMode(videoTrack: MediaStreamTrack, config: VideoProcessingConfig): Promise<void> {
        infoLog?.log('Starting RPC fallback mode...');

        if (this.hasMSTPInWindow()) {
            try {
                this.processor = new MediaStreamTrackProcessor({ track: videoTrack });
                this.frameReader = this.processor.readable.getReader();
            } catch (error) {
                errorLog?.log('MSTP creation failed, falling back to canvas:', error);
                this.frameReader = this.createCanvasFrameExtractor(videoTrack);
            }
        } else {
            this.frameReader = this.createCanvasFrameExtractor(videoTrack);
        }

        await this.worker.initialize(config, { type: 'rpc-timeout', timeoutMs: 15000 });
        void this.pumpFrames();
        infoLog?.log('RPC fallback mode started');
    }

    public async stop(): Promise<void> {
        infoLog?.log('Stopping pipeline...');

        // Unsubscribe from VAD
        if (this.vadSubscription) {
            this.vadSubscription.unsubscribe();
            this.vadSubscription = null;
        }
        if (this.vadSilenceTimer !== null) {
            clearTimeout(this.vadSilenceTimer);
            this.vadSilenceTimer = null;
        }
        this.isSpeaking = true;

        // Unsubscribe from clock
        if (this.clockUnsubscribe) {
            this.clockUnsubscribe();
            this.clockUnsubscribe = null;
        }

        // Stop stats polling
        if (this.statsInterval) { clearInterval(this.statsInterval); this.statsInterval = null; }
        if (this.diagnosticsInterval) { clearInterval(this.diagnosticsInterval); this.diagnosticsInterval = null; }

        // Stop frame pump (RPC fallback)
        this.processing = false;
        if (this.frameReader) {
            try { await this.frameReader.cancel(); } catch { /* ignore */ }
            this.frameReader = null;
        }

        // Stop worker (handles encoder, segmentation, RPC cleanup internally)
        await this.worker.stop();
        infoLog?.log('Worker stopped');

        // Cleanup
        if (this._disconnectApiHandler) {
            Api.onDisconnectRequested(WorkerKind.VideoCapture).remove(this._disconnectApiHandler);
            this._disconnectApiHandler = null;
        }
        this.worker.dispose();
        this.workerInstance.terminate();

        infoLog?.log('Pipeline stopped');
    }

    async reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void> {
        // Don't reconfigure while server-paused — wait for resumeEncoding
        if (this.serverPaused) return;

        infoLog?.log(`Reconfiguring: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);

        this.config.encoderConfig.bitrate = params.bitrate;
        this.config.encoderConfig.width = params.width;
        this.config.encoderConfig.height = params.height;
        this.savedBitrate = params.bitrate;

        // If currently in VAD silence, apply reduced ratio
        if (!this.isSpeaking && this.config.adaptiveFramerate?.enabled) {
            const ratio = this.config.adaptiveFramerate.reducedBitrateRatio ?? 0.25;
            const reducedBitrate = Math.round(params.bitrate * ratio);
            debugLog?.log(`reconfigure during silence: applying reduced bitrate ${reducedBitrate}`);
            await this.worker.reconfigure({ ...params, bitrate: reducedBitrate });
            return;
        }

        await this.worker.reconfigure(params);
    }

    async switchCodec(newCodecString: string): Promise<void> {
        if (newCodecString === this.config.encoderConfig.codec) {
            infoLog?.log(`switchCodec: already using ${newCodecString}, skipping`);
            return;
        }

        infoLog?.log(`Switching codec from ${this.config.encoderConfig.codec} to ${newCodecString}`);

        const newEncoderConfig: EncoderConfig = { ...this.config.encoderConfig, codec: newCodecString };
        this.config.encoderConfig = newEncoderConfig;

        // Worker handles VideoStream completion + encoder switch internally
        await this.worker.switchCodec(newEncoderConfig);

        infoLog?.log(`Codec switched to ${newCodecString}`);
    }

    async toggleBlur(enabled: boolean, segmentationConfig?: SegmentationConfig): Promise<void> {
        infoLog?.log(`Toggling background blur: ${enabled ? 'ON' : 'OFF'}`);

        if (enabled && !this.config.backgroundBlur && !segmentationConfig) {
            throw new Error('Cannot enable blur: no segmentation config');
        }

        if (!this.config.backgroundBlur && segmentationConfig) {
            this.config.backgroundBlur = { enabled: true, segmentationConfig };
        }

        if (this.config.backgroundBlur) {
            this.config.backgroundBlur.enabled = enabled;
        }

        // Worker handles lazy ONNX loading and blur toggle internally
        await this.worker.toggleBlur(enabled, segmentationConfig);

        infoLog?.log(`Background blur ${enabled ? 'enabled' : 'disabled'}`);
    }

    async switchSegmentationBackend(newBackend: 'webgpu' | 'wasm'): Promise<void> {
        infoLog?.log(`Switching segmentation backend to: ${newBackend}`);
        if (this.config.backgroundBlur?.segmentationConfig) {
            this.config.backgroundBlur.segmentationConfig.backend = newBackend;
        }
        // Worker would need a restart for backend change — for now, toggle blur off/on
        await this.worker.toggleBlur(false);
        await this.worker.toggleBlur(true, this.config.backgroundBlur?.segmentationConfig);
    }

    getEncoderStats(): EncoderStats {
        return { ...this.currentStats.encoder };
    }

    getSegmentationStats(): SegmentationStats | null {
        return this.currentStats.segmentation ? { ...this.currentStats.segmentation } : null;
    }

    getOrientationStats(): OrientationStats | null {
        return this.currentStats.orientation ? { ...this.currentStats.orientation } : null;
    }

    setPreviewCallback(callback: ((frame: VideoFrame) => void) | null): void {
        this.previewCallback = callback;
    }

    updateSavedBitrate(bitrate: number): void {
        this.savedBitrate = bitrate;
        if (!this.isSpeaking) {
            const ratio = this.config.adaptiveFramerate?.reducedBitrateRatio ?? 0.25;
            const reducedBitrate = Math.round(this.savedBitrate * ratio);
            void this.worker.reconfigure({
                bitrate: reducedBitrate,
                width: this.config.encoderConfig.width,
                height: this.config.encoderConfig.height,
            });
        }
    }

    // ─── VAD ────────────────────────────────────────────────────────────────

    subscribeToVad(): void {
        if (this.vadSubscription) return;

        this.vadSubscription = RecorderStateHub.recorderStateChanged$.subscribe(state => {
            const active = !state.isRecording || state.isVoiceActive;
            this.setVadActive(active);
        });

        const current = RecorderStateHub.getState();
        this.setVadActive(!current.isRecording || current.isVoiceActive);
        debugLog?.log('Subscribed to VAD for adaptive framerate');
    }

    setRemoteStreamCount(count: number): void {
        const wasGroup = this.remoteStreamCount >= 2;
        this.remoteStreamCount = count;
        const isGroup = count >= 2;
        debugLog?.log('setRemoteStreamCount:', count);

        // Forward to worker
        void this.worker.setVadState(this.isSpeaking, count);

        // Transitioning from group → non-group: restore
        if (wasGroup && !isGroup) {
            if (this.vadSilenceTimer !== null) {
                clearTimeout(this.vadSilenceTimer);
                this.vadSilenceTimer = null;
            }
            if (!this.isSpeaking) {
                this.isSpeaking = true;
                void this.worker.reconfigure({
                    bitrate: this.savedBitrate,
                    width: this.config.encoderConfig.width,
                    height: this.config.encoderConfig.height,
                });
                void this.worker.forceKeyFrame();
            }
        }
    }

    /**
     * Server-driven pause: stop encoding but keep camera stream alive.
     * Called when the priority evaluator pauses this stream.
     */
    pauseEncoding(): void {
        if (this.serverPaused) return;
        this.serverPaused = true;
        infoLog?.log('Server pause: stopping encoder');

        // Reduce to zero bitrate and tell worker to stop encoding
        void this.worker.reconfigure({
            bitrate: 0,
            width: this.config.encoderConfig.width,
            height: this.config.encoderConfig.height,
        });
    }

    /**
     * Server-driven resume: restart encoding after pause.
     * Called when the priority evaluator un-pauses this stream.
     */
    resumeEncoding(): void {
        if (!this.serverPaused) return;
        this.serverPaused = false;
        infoLog?.log('Server resume: restarting encoder');

        void this.worker.reconfigure({
            bitrate: this.savedBitrate,
            width: this.config.encoderConfig.width,
            height: this.config.encoderConfig.height,
        });
        void this.worker.forceKeyFrame();
    }

    /**
     * Force a key frame to be generated by the encoder.
     * This can be useful for resetting the video stream or ensuring smooth transitions.
     */
    forceKeyFrame(): Promise<void> {
        return this.worker.forceKeyFrame();
    }

    private setVadActive(isActive: boolean): void {
        if (isActive) {
            if (this.vadSilenceTimer !== null) {
                clearTimeout(this.vadSilenceTimer);
                this.vadSilenceTimer = null;
            }

            if (!this.isSpeaking) {
                this.isSpeaking = true;
                debugLog?.log('VAD: speech resumed');

                void this.worker.reconfigure({
                    bitrate: this.savedBitrate,
                    width: this.config.encoderConfig.width,
                    height: this.config.encoderConfig.height,
                });
                void this.worker.forceKeyFrame();
                void this.worker.setVadState(true, this.remoteStreamCount);
            }
        } else {
            if (this.isSpeaking && this.vadSilenceTimer === null && this.remoteStreamCount >= 2) {
                const delay = this.config.adaptiveFramerate?.silenceDelayMs ?? 60_000;
                this.vadSilenceTimer = setTimeout(() => {
                    this.vadSilenceTimer = null;
                    this.isSpeaking = false;

                    const ratio = this.config.adaptiveFramerate?.reducedBitrateRatio ?? 0.25;
                    const reducedBitrate = Math.round(this.savedBitrate * ratio);
                    debugLog?.log(`VAD: silence, reducing bitrate to ${reducedBitrate}`);

                    void this.worker.reconfigure({
                        bitrate: reducedBitrate,
                        width: this.config.encoderConfig.width,
                        height: this.config.encoderConfig.height,
                    });
                    void this.worker.setVadState(false, this.remoteStreamCount);
                }, delay);
            }
        }
    }

    // ─── Encoder failure ──────────────────────────────────────────────────

    private handleEncoderFailure(codec: string): void {
        errorLog?.log(`Encoder failed for codec ${codec}`);
        if (this.onEncoderFailure)
            this.onEncoderFailure(codec);
    }

    // ─── Backpressure ───────────────────────────────────────────────────────

    private handleEncoderBackpressure(dropRate: number): void {
        const now = performance.now();
        if (now - this.lastBackpressureStepDown < 5000) return;

        const currentBitrate = this.config.encoderConfig.bitrate;
        const currentWidth = this.config.encoderConfig.width;
        const currentHeight = this.config.encoderConfig.height;

        let target: { width: number; height: number; bitrate: number } | null = null;
        if (currentWidth > 1280) target = { width: 1280, height: 720, bitrate: 4_000_000 };
        else if (currentWidth > 960) target = { width: 960, height: 540, bitrate: 2_500_000 };
        else if (currentWidth > 640) target = { width: 640, height: 360, bitrate: 1_000_000 };
        if (!target && currentBitrate > 500_000) {
            target = { width: currentWidth, height: currentHeight, bitrate: Math.round(currentBitrate * 0.5) };
        }

        if (!target) {
            warnLog?.log('Backpressure step-down: already at minimum quality');
            return;
        }

        this.backpressureStepDownCount++;
        this.lastBackpressureStepDown = now;
        warnLog?.log(`Backpressure step-down #${this.backpressureStepDownCount}: ` +
            `${currentWidth}x${currentHeight}@${(currentBitrate / 1e6).toFixed(1)}Mbps → ` +
            `${target.width}x${target.height}@${(target.bitrate / 1e6).toFixed(1)}Mbps, dropRate=${(dropRate * 100).toFixed(0)}%`);

        void this.reconfigure(target);
    }

    // ─── Canvas fallback ────────────────────────────────────────────────────

    private createCanvasReadableStream(videoTrack: MediaStreamTrack): ReadableStream<VideoFrame> {
        infoLog?.log('Creating canvas-based ReadableStream (Safari fallback)');
        const canvas = document.createElement('canvas');
        const video = document.createElement('video');
        video.autoplay = true;
        video.muted = true;
        video.playsInline = true;
        video.srcObject = new MediaStream([videoTrack]);

        const framerate = this.config.encoderConfig.framerate;
        const interval = 1000 / framerate;
        let pumpInterval: number | null = null;
        let videoReady = false;

        return new ReadableStream<VideoFrame>({
            start: (controller) => {
                const pump = () => {
                    if (!this.processing) { controller.close(); return; }
                    if (!videoReady || video.paused || video.ended) {
                        pumpInterval = window.setTimeout(pump, 100);
                        return;
                    }
                    if (canvas.width !== video.videoWidth || canvas.height !== video.videoHeight) {
                        canvas.width = video.videoWidth;
                        canvas.height = video.videoHeight;
                    }
                    const ctx = canvas.getContext('2d', { willReadFrequently: true });
                    if (ctx && video.videoWidth > 0 && video.videoHeight > 0) {
                        try {
                            ctx.drawImage(video, 0, 0);
                            controller.enqueue(new VideoFrame(canvas, { timestamp: performance.now() * 1000 }));
                        } catch (error) {
                            errorLog?.log('Canvas frame extraction error:', error);
                        }
                    }
                    pumpInterval = window.setTimeout(pump, interval);
                };

                video.onloadedmetadata = () => {
                    const playPromise = video.play().catch(() => { videoReady = true; pump(); });
                    void playPromise.then(() => { videoReady = true; pump(); }).catch(() => { /* ignore */ });
                };
                video.onerror = (e) => errorLog?.log('Canvas extractor video error:', e);
            },
            cancel: () => {
                if (pumpInterval) { clearTimeout(pumpInterval); pumpInterval = null; }
            },
        });
    }

    private createCanvasFrameExtractor(videoTrack: MediaStreamTrack): ReadableStreamDefaultReader<VideoFrame> {
        return this.createCanvasReadableStream(videoTrack).getReader();
    }

    /**
     * RPC fallback: pump frames from reader to worker via per-frame RPC.
     */
    private async pumpFrames(): Promise<void> {
        infoLog?.log('Starting frame pump (RPC fallback)...');
        let frameCount = 0;

        try {
            while (this.processing) {
                const { done, value: frame } = await this.frameReader!.read();
                if (done) break;
                frameCount++;

                try {
                    await this.worker.encodeFrame(frame);
                } catch {
                    try { frame.close(); } catch { /* already transferred */ }
                }
            }
        } catch (error) {
            if (this.processing) errorLog?.log('Frame pump error:', error);
        }
        infoLog?.log(`Frame pump stopped after ${frameCount} frames`);
    }

    private hasMSTPInWindow(): boolean {
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-explicit-any
        return typeof (globalThis as any).MediaStreamTrackProcessor === 'function';
    }

    private supportsTrackTransfer(): boolean {
        // Safari 18+ supports transferring MediaStreamTrack to workers.
        // Test by trying to transfer a track through a MessageChannel.
        try {
            const canvas = document.createElement('canvas');
            canvas.width = 1;
            canvas.height = 1;
            const stream = canvas.captureStream(0);
            const track = stream.getVideoTracks()[0];
            const mc = new MessageChannel();
            mc.port1.postMessage(track, [track as unknown as Transferable]);
            mc.port1.close();
            mc.port2.close();
            return true;
        } catch {
            return false;
        }
    }
}

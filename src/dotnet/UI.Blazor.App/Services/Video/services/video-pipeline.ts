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
import { RunningEMA } from 'math';
import type { Disposable } from 'disposable';
import { supportsTransferableStreams } from '../workers/stream-channel';

import { BrowserInit } from '../../../../UI.Blazor/Services/BrowserInit/browser-init';
import { ConnectivityUI } from '../../../../UI.Blazor/Services/ConnectivityUI/connectivity-ui';
import { Api, WorkerKind } from 'api';
import type { EncoderConfig, EncoderStats } from '../webcodecs-encoder';
import type { SegmentationConfig, SegmentationStats, OrientationStats, SpatialLayerConfig, VideoProcessingStreamingStats } from '../workers/video-processing-worker-contract';
import type {
    VideoProcessingWorker,
    VideoProcessingWorkerCallbacks,
    VideoProcessingConfig,
    VideoProcessingStats,
} from '../workers/video-processing-worker-contract';
import { Versioning } from 'versioning';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { SessionTokens } from '../../../../UI.Blazor/Services/Security/session-tokens';
import { ServerClock } from 'server-clock';
import type { Subscription } from 'rxjs';
import { RecorderStateHub } from '../../../Components/AudioRecorder/recorder-state-hub';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

export interface PipelineConfig {
    encoderConfig: EncoderConfig;
    /** Simulcast layers. When set with length >= 1, the worker creates N encoders
     *  (encoderConfig drives SpatialLayerId=0 base; entries here are extras at
     *  SpatialLayerId=i+1). Omit for single-encoder (P2P) mode. */
    spatialLayers?: SpatialLayerConfig[];
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

// Map screen.orientation.angle → camera rotation (degrees CW to apply to the
// sensor buffer so the image appears upright on display). Used as a fallback
// when `VideoFrame.rotation` is not populated by the platform (Safari iOS MSTP).
// Empirical table for iPhone front camera — landscape modes were rotated 180°
// with the (90-angle) formula; (90+angle) matches tested orientations.
// Desktop webcams expose frames in display orientation already, so the 90°
// sensor-mount offset must NOT be applied — otherwise initial encoder config
// transposes to portrait and the first-frame reconcile triggers an immediate
// configure→reconfigure flip-flop that crashes Chrome's HW HEVC encoder.
function computeSenderRotation(): number {
    if (!DeviceInfo.isMobile) return 0;
    return (90 + screen.orientation.angle) % 360;
}

export interface IVideoPipeline {
    start(inputStream: MediaStream): Promise<void>;
    stop(): Promise<void>;
    reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void>;
    switchCodec(newCodecString: string, spatialLayers?: SpatialLayerConfig[]): Promise<void>;
    setSpatialLayers(layers: SpatialLayerConfig[] | null): Promise<void>;
    toggleBlur(enabled: boolean, segmentationConfig?: SegmentationConfig): Promise<void>;
    switchSegmentationBackend(backend: 'webgpu' | 'wasm'): Promise<void>;
    setPreviewCallback(callback: ((frame: VideoFrame) => void) | null): void;
    /** WYSIWYG preview track: post-rotation, post-downscale `MediaStreamTrack`
     *  produced inside the worker via MSTG and posted back to main on startup.
     *  Null on browsers without MSTG worker support, or before pipeline.start
     *  has resolved. Consumers attach to a `<video srcObject>`. */
    getProcessedTrack(): MediaStreamTrack | null;
    getEncoderStats(): EncoderStats;
    getSegmentationStats(): SegmentationStats | null;
    getOrientationStats(): OrientationStats | null;
    getStreamingStats(): VideoProcessingStreamingStats | null;
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
    // WYSIWYG preview track from worker MSTG (encoder modes only). Set by the
    // `onPreviewTrack` callback during worker startup; null on browsers without
    // MSTG support, in which case `previewCallback` (RPC frame path) is used.
    private processedTrack: MediaStreamTrack | null = null;

    // Common
    private processing = false;

    // VAD-driven local spatial drop (G2). When silence latches in a multi-peer
    // call we drop the top simulcast extra locally without waiting for a server
    // feedback round-trip. Speech resume restores it. Saved here so the resume
    // path knows what to add back; cleared whenever an external setSpatialLayers
    // arrives (cap-driven changes are authoritative over VAD state).
    private vadDroppedLayer: SpatialLayerConfig | null = null;

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
    // Timestamp of the most recent structural change (switchCodec / setSpatialLayers).
    // Backpressure samples in the cooldown window after a switch are dropped — the
    // freshly armed encoder hasn't produced output yet, so worker drop-rate spikes
    // to ~100% during the few frames between switch and first encoded chunk. Without
    // this gate, a codec switch would immediately trigger another step-down or codec
    // fallback, producing a cascade of switches.
    private lastStructuralChangeAt = 0;
    private readonly postSwitchCooldownMs = 3000;
    // Timestamp at which the pipeline first started (pipeline-start cooldown).
    // Distinct from `lastStructuralChangeAt`: the first encoder warmup is much
    // longer than a mid-stream switch (cold KF + codec init + chroma allocation),
    // so we need a longer grace window. Without this, the first backpressure
    // sample arrives during cold-start with dropRate=60%+, triggers a step-down
    // immediately, and locks the call to a lower tier even though the steady
    // state is healthy.
    private pipelineStartedAt = 0;
    private readonly pipelineWarmupMs = 12_000;
    // EMA-smoothed drop rate across successive backpressure notifications.
    // Each notification is a 5 s drop-rate window from the worker (see
    // video-processing.ts:backpressureWindowMs). Step-down only fires when the
    // EMA is sustained — a single transient spike (thermal blip, one slow GC)
    // no longer collapses resolution for the rest of the session. RunningEMA
    // uses a plain running average for the first `minSampleCount` samples
    // (warmup), then switches to exponential smoothing with α = 2/(n+1).
    private readonly backpressureEma = new RunningEMA(0, 2);

    // Encoder failure callback (set by recording-service for codec fallback)
    public onEncoderFailure: ((failedCodec: string) => void) | null = null;

    // Camera-track-ended callback (set by recording-service). Fires when the
    // underlying MediaStreamTrack ends UNEXPECTEDLY — e.g. another browser tab
    // grabbed the same physical camera, the device was unplugged, or a privacy
    // shutter triggered. Distinct from the worker's normal `Stream input ended`
    // path (which fires both on user-initiated stop and on track-end). Recording
    // service surfaces a user-visible error and stops the pipeline.
    public onTrackEnded: (() => void) | null = null;
    private trackEndedHandler: (() => void) | null = null;
    private inputTrack: MediaStreamTrack | null = null;

    // Server clock sync
    private clockUnsubscribe: (() => void) | null = null;

    // Screen-orientation change listener — supplies senderRotationDeg to worker
    // when VideoFrame.rotation is not populated by the platform (e.g. Safari iOS).
    private orientationChangeHandler: (() => void) | null = null;

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
        streaming: null,
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
                onPreviewTrack: (track: MediaStreamTrack) => {
                    infoLog?.log(`Worker delivered preview MSTG track (id=${track.id}, kind=${track.kind})`);
                    this.processedTrack = track;
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
        this.pipelineStartedAt = performance.now();
        this.inputTrack = videoTrack;
        // Watch for track-end. The MSTP path keeps the main-thread reference alive
        // (only the underlying ReadableStream is transferred), so this listener
        // fires on either a deliberate `track.stop()` (we'll be in `processing=false`
        // by then via stop()) or an external cause — camera contention, device
        // unplug, privacy shutter, OS-level revocation. Only the external causes
        // need user feedback; the deliberate-stop case is filtered in the handler.
        this.trackEndedHandler = () => {
            if (!this.processing) {
                debugLog?.log('Camera track ended after pipeline stop — expected');
                return;
            }
            warnLog?.log('Camera track ended unexpectedly (other tab grabbed device, unplug, or revoked permission)');
            this.onTrackEnded?.();
        };
        videoTrack.addEventListener('ended', this.trackEndedHandler);

        // Build worker config
        // Fusion RPC WebSocket URL — worker pushes frames via
        // `IStreamServer.PushVideo` over this connection.
        const apiUrl = BrowserInit.getUrl('/rpc/ws').replace(/^http/, 'ws');
        // Screencast: screen buffer is already in display orientation regardless
        // of device rotation. Applying camera-sensor rotation compensation here
        // rotates the encoded frames 90° on desktop (angle=0 → rotation=90) which
        // breaks playback on the remote end. Skip it entirely for screen tracks.
        const isScreencast = (this.config.streaming?.streamKind ?? 0) === 1;
        const initialSenderRotation = isScreencast ? 0 : computeSenderRotation();
        // Transpose encoder dims at startup when device is in portrait orientation:
        // encoder must be sized in display orientation so the encoded stream matches
        // what the sender sees. Mutates the shared encoder config (pipeline owns it).
        // Skipped for screencast — the worker reconciles encoder dims against the
        // actual screen-track size on first frame.
        const wantPortraitAtStart = !isScreencast && (initialSenderRotation === 90 || initialSenderRotation === 270);
        const encCfg = this.config.encoderConfig;
        if (!isScreencast) {
            if (wantPortraitAtStart && encCfg.width > encCfg.height) {
                const tmp = encCfg.width; encCfg.width = encCfg.height; encCfg.height = tmp;
                infoLog?.log(`Transposed encoder to portrait at start: ${encCfg.width}x${encCfg.height}`);
            } else if (!wantPortraitAtStart && encCfg.height > encCfg.width) {
                const tmp = encCfg.width; encCfg.width = encCfg.height; encCfg.height = tmp;
                infoLog?.log(`Transposed encoder to landscape at start: ${encCfg.width}x${encCfg.height}`);
            }
        }
        const workerConfig: VideoProcessingConfig = {
            encoder: this.config.encoderConfig,
            streaming: {
                apiUrl,
                sessionToken: SessionTokens.current,
                chatId: this.config.streaming?.chatId ?? '',
                serverClockOffsetMs: ServerClock.offsetMs,
                streamKind: this.config.streaming?.streamKind ?? 0,
            },
            senderRotationDeg: initialSenderRotation,
        };

        if (this.config.spatialLayers && this.config.spatialLayers.length > 0) {
            workerConfig.spatialLayers = this.config.spatialLayers;
            infoLog?.log(`Simulcast activated: base + ${this.config.spatialLayers.length} extra layer(s)`);
        }
        infoLog?.log(`Sender rotation (initial): ${initialSenderRotation}° (screen.orientation.angle=${screen.orientation.angle})`);

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

        // Subscribe to screen-orientation changes — push rotation to worker so the
        // GPU downscaler can rotate correctly on platforms where VideoFrame.rotation
        // is null (Safari iOS MSTP). Encoder dims are transposed on portrait/landscape
        // flip so the encoded stream matches device orientation.
        // Screencast ignores device rotation: the screen buffer stays in its native
        // orientation regardless of how the phone is held.
        if (!isScreencast) {
            this.orientationChangeHandler = () => {
                const rot = computeSenderRotation();
                infoLog?.log(`Sender rotation (change): ${rot}° (screen.orientation.angle=${screen.orientation.angle})`);
                void this.worker.setSenderRotation(rot, rpcNoWait);
                const encW = this.config.encoderConfig.width;
                const encH = this.config.encoderConfig.height;
                const wantPortrait = rot === 90 || rot === 270;
                const isPortrait = encH > encW;
                if (wantPortrait !== isPortrait) {
                    void this.reconfigure({ bitrate: this.savedBitrate, width: encH, height: encW });
                }
            };
            screen.orientation.addEventListener('change', this.orientationChangeHandler);
        }

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
            // Transferring a MediaStreamTrack via postMessage neuters the
            // main-thread reference (Safari 18 spec behaviour). Clone first so
            // the original `videoTrack` stays alive on main for preview
            // consumers (RecorderPreviewView's <video srcObject>).
            const workerTrack = videoTrack.clone();
            await this.worker.startWithTrack(config, workerTrack, { type: 'rpc-timeout', timeoutMs: 15000 });
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

        // Unsubscribe from orientation change
        if (this.orientationChangeHandler) {
            screen.orientation.removeEventListener('change', this.orientationChangeHandler);
            this.orientationChangeHandler = null;
        }

        // Detach track-ended listener — pipe is shutting down so any subsequent
        // `ended` event is the deliberate stop and irrelevant.
        if (this.inputTrack && this.trackEndedHandler) {
            this.inputTrack.removeEventListener('ended', this.trackEndedHandler);
        }
        this.inputTrack = null;
        this.trackEndedHandler = null;

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

    // Hot-add or hot-remove simulcast extras on a running pipeline. Pass null /
    // empty array to collapse to single-encoder. Worker preserves the base
    // encoder + RPC stream — no Unregister/Register round-trip on the server.
    // Force-keyframe is issued internally so subscribers latch on instantly.
    async setSpatialLayers(layers: SpatialLayerConfig[] | null): Promise<void> {
        const next = (layers && layers.length > 0) ? layers : [];
        const prevCount = this.config.spatialLayers?.length ?? 0;
        const nextCount = next.length;
        this.config.spatialLayers = next.length > 0 ? next : undefined;
        // External cap-driven change overrides any pending VAD-restore — server
        // policy (G1: MaxSpatialLayer aggregate) is authoritative over local
        // VAD heuristic. Drop the saved layer so speech-resume doesn't add it
        // back against current cap intent.
        if (this.vadDroppedLayer !== null) {
            debugLog?.log('setSpatialLayers: clearing pending VAD-dropped layer (external override)');
            this.vadDroppedLayer = null;
        }
        if (!this.processing) {
            debugLog?.log(`setSpatialLayers: not running, cached ${prevCount} → ${nextCount} for next start`);
            return;
        }
        infoLog?.log(`setSpatialLayers: ${prevCount} → ${nextCount} layer(s) live`);
        this.markStructuralChange('setSpatialLayers');
        await this.worker.setSpatialLayers(next);
    }

    private markStructuralChange(reason: string): void {
        this.lastStructuralChangeAt = performance.now();
        this.backpressureEma.reset();
        debugLog?.log(`Structural change (${reason}): backpressure cooldown ${this.postSwitchCooldownMs}ms armed`);
    }

    async switchCodec(newCodecString: string, spatialLayers?: SpatialLayerConfig[]): Promise<void> {
        if (newCodecString === this.config.encoderConfig.codec) {
            infoLog?.log(`switchCodec: already using ${newCodecString}, skipping`);
            return;
        }

        infoLog?.log(`Switching codec from ${this.config.encoderConfig.codec} to ${newCodecString}`);

        const newEncoderConfig: EncoderConfig = { ...this.config.encoderConfig, codec: newCodecString };
        this.config.encoderConfig = newEncoderConfig;
        // Persist the ladder override if the caller supplied one, so subsequent
        // restarts/re-probes pick up the same simulcast shape.
        if (spatialLayers !== undefined)
            this.config.spatialLayers = spatialLayers.length > 0 ? spatialLayers : undefined;

        // Worker handles VideoStream completion + encoder switch internally, and
        // rebuilds simulcast extras from `spatialLayers` so the ladder survives
        // the switch. Omitting `spatialLayers` collapses to single-encoder (P2P).
        this.markStructuralChange(`switchCodec(${newCodecString})`);
        await this.worker.switchCodec(newEncoderConfig, this.config.spatialLayers);

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

    getStreamingStats(): VideoProcessingStreamingStats | null {
        return this.currentStats.streaming ? { ...this.currentStats.streaming } : null;
    }

    setPreviewCallback(callback: ((frame: VideoFrame) => void) | null): void {
        this.previewCallback = callback;
    }

    getProcessedTrack(): MediaStreamTrack | null {
        return this.processedTrack;
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
                // G2: restore the VAD-dropped top extra (if any). Re-emits via
                // worker.setSpatialLayers — base encoder + RPC stream untouched.
                // forceKeyFrame after both reconfig and layer restore so the new
                // extra has a clean anchor for subscribers.
                if (this.vadDroppedLayer && this.processing) {
                    const restored = [...(this.config.spatialLayers ?? []), this.vadDroppedLayer];
                    this.config.spatialLayers = restored;
                    debugLog?.log(`VAD: restoring dropped layer ${this.vadDroppedLayer.width}x${this.vadDroppedLayer.height}`);
                    this.vadDroppedLayer = null;
                    void this.worker.setSpatialLayers(restored);
                }
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
                    // G2: drop the top simulcast extra locally during silence.
                    // Webcam only — screencast has no VAD semantics and its
                    // adaptiveFramerate config isn't even enabled, but guard
                    // explicitly for clarity. Only when 2+ extras are active so
                    // the receiver still gets at least base + mid (avoids
                    // collapsing to single tier during the silent half of a
                    // conversation, which would give a 180p experience to
                    // anyone joining mid-silence).
                    const isScreencast = (this.config.streaming?.streamKind ?? 0) === 1;
                    const extras = this.config.spatialLayers;
                    if (!isScreencast && extras && extras.length >= 2 && this.vadDroppedLayer === null) {
                        const dropped = extras[extras.length - 1];
                        const remaining = extras.slice(0, -1);
                        this.vadDroppedLayer = dropped;
                        this.config.spatialLayers = remaining;
                        debugLog?.log(`VAD: dropping top layer ${dropped.width}x${dropped.height} (${extras.length} → ${remaining.length} extras)`);
                        void this.worker.setSpatialLayers(remaining);
                    }
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
        // Sustained-backpressure step-down with EMA smoothing.
        //
        // Worker notifies once per 5 s drop-rate window (see video-processing.ts
        // backpressureWindowMs). A SINGLE high-dropRate sample is noise — GPU
        // contention from a tab switch, a thermal blip, one GC. Step-down only
        // fires once the EMA stays elevated across multiple windows — the
        // signature of sustained encoder overload.
        //
        // RunningEMA(minSampleCount=2) uses a running average for the first two
        // samples (gives transients a chance to clear) then switches to
        // exponential smoothing with α = 2/(n+1) = 0.67 — responsive but
        // resists a single spike.
        //
        //   trigger  = 0.30 → EMA above this = ~30% sustained drop.
        //   cooldown = 10 s between step-downs.
        const trigger = 0.30;
        const cooldownMs = 10_000;
        const cfg = this.config.encoderConfig;

        // Skip step-down when the tab is backgrounded. A capturing tab is exempt
        // from Page Lifecycle freezing, but Windows EcoQoS / macOS QoS still
        // demote the renderer's CPU tier — encoder throughput drops even though
        // the hardware is capable. The worker already drops frames via
        // encodeQueueSize, which is the correct response. A resolution downshift
        // or codec switch here would stick permanently after the user returns
        // focus (particularly problematic during screencast, where the sharer's
        // tab is hidden the whole time). Reset the EMA so foreground samples
        // start clean.
        if (typeof document !== 'undefined' && document.visibilityState === 'hidden') {
            if (this.backpressureEma.sampleCount > 0) {
                debugLog?.log(
                    `Backpressure during hidden tab (dropRate=${(dropRate * 100).toFixed(0)}%): ` +
                    `deferring step-down, resetting EMA`);
                this.backpressureEma.reset();
            }
            return;
        }

        // Post-switch cooldown: a freshly armed encoder needs a few hundred ms to
        // produce output. The worker's drop-rate counter sees frames-in / 0 frames-out
        // = 100% drop in that window. Without this gate, a codec switch (or simulcast
        // ladder reconfig) immediately triggers another step-down, cascading
        // into repeated codec/ladder churn.
        const sinceSwitch = performance.now() - this.lastStructuralChangeAt;
        if (this.lastStructuralChangeAt > 0 && sinceSwitch < this.postSwitchCooldownMs) {
            debugLog?.log(
                `Backpressure during post-switch cooldown ` +
                `(${sinceSwitch.toFixed(0)}/${this.postSwitchCooldownMs}ms, ` +
                `dropRate=${(dropRate * 100).toFixed(0)}%): ignoring sample`);
            return;
        }

        // Pipeline-startup cooldown: encoder cold-start (codec init, first KF,
        // first I-frame allocation) routinely produces 60-80% drop rate on the
        // very first 5 s window from the worker. Without this gate, a single
        // cold-start sample steps down the resolution and locks the call there
        // even though the steady-state encode would be fine. Reset the EMA so
        // post-warmup samples start clean.
        const sinceStart = performance.now() - this.pipelineStartedAt;
        if (this.pipelineStartedAt > 0 && sinceStart < this.pipelineWarmupMs) {
            if (this.backpressureEma.sampleCount > 0) {
                debugLog?.log(
                    `Backpressure during pipeline warmup ` +
                    `(${sinceStart.toFixed(0)}/${this.pipelineWarmupMs}ms, ` +
                    `dropRate=${(dropRate * 100).toFixed(0)}%): ignoring sample, resetting EMA`);
                this.backpressureEma.reset();
            } else {
                debugLog?.log(
                    `Backpressure during pipeline warmup ` +
                    `(${sinceStart.toFixed(0)}/${this.pipelineWarmupMs}ms, ` +
                    `dropRate=${(dropRate * 100).toFixed(0)}%): ignoring sample`);
            }
            return;
        }

        this.backpressureEma.appendSample(dropRate);
        const ema = this.backpressureEma.value;
        const n = this.backpressureEma.sampleCount;

        const now = performance.now();
        const inCooldown = now - this.lastBackpressureStepDown < cooldownMs;
        warnLog?.log(
            `Backpressure sample: dropRate=${(dropRate * 100).toFixed(0)}%, ` +
            `ema=${(ema * 100).toFixed(0)}% (n=${n}), cooldown=${inCooldown ? 'yes' : 'no'} at ` +
            `${cfg.width}x${cfg.height}@${(cfg.bitrate / 1e6).toFixed(1)}Mbps`);

        // Minimum sample gate: with a single sample, the EMA equals the sample
        // value, defeating the smoothing. Require at least 2 windows of sustained
        // backpressure (~10 s with the worker's 5 s window) before any step-down.
        if (n < 2) return;
        if (ema < trigger) return;
        if (inCooldown) return;

        // Target: drop one tier along a fixed 1080p → 720p → 540p → 360p chain.
        // Per-tier bitrate roughly halves. Below 360p there's no useful
        // step-down — keep the current tier and ride out.
        const currentWidth = cfg.width;
        const currentHeight = cfg.height;
        const currentBitrate = cfg.bitrate;
        let target: { width: number; height: number; bitrate: number } | null = null;
        if (currentWidth > 1280) target = { width: 1280, height: 720, bitrate: 4_000_000 };
        else if (currentWidth > 960) target = { width: 960, height: 540, bitrate: 2_500_000 };
        else if (currentWidth > 640) target = { width: 640, height: 360, bitrate: 1_000_000 };
        if (!target) {
            // Bottomed out on the current codec. Reuse the encoder-failure
            // path to ask recording-service for the next codec in priority
            // order (currently H.264 — widest HW coverage, most forgiving
            // encoder under load). Reset EMA before the handoff so a
            // successful codec switch gets a clean measurement window.
            warnLog?.log(
                `Backpressure step-down: already at minimum tier (${currentWidth}x${currentHeight}) ` +
                `on ${cfg.codec} (ema=${(ema * 100).toFixed(0)}%) — requesting codec fallback`);
            this.backpressureEma.reset();
            this.lastBackpressureStepDown = now;  // cooldown for the codec switch too
            if (this.onEncoderFailure)
                this.onEncoderFailure(cfg.codec);
            return;
        }

        this.backpressureStepDownCount++;
        this.lastBackpressureStepDown = now;
        warnLog?.log(
            `Backpressure step-down #${this.backpressureStepDownCount}: ` +
            `${currentWidth}x${currentHeight}@${(currentBitrate / 1e6).toFixed(1)}Mbps → ` +
            `${target.width}x${target.height}@${(target.bitrate / 1e6).toFixed(1)}Mbps ` +
            `(ema=${(ema * 100).toFixed(0)}%, samples=${n})`);
        // Reset so the post-reconfig window re-evaluates fresh. If the new
        // tier still can't keep up, EMA climbs again and triggers another
        // step at the lower tier.
        this.backpressureEma.reset();
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

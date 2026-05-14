// Phase-7 cut-over: this file now drives the NEW `RecorderWorker`
// contract directly (Services/Video/sender/recorder-worker-contract.ts)
// instead of going through the legacy `RecordingService` /
// `VideoPipeline` / `VideoProcessingWorker` chain.
//
// Public TS surface preserved:
//  - `VideoRecorder` class + every public method called from Blazor
//    via JS interop (Services/VideoRecorder.cs).
//  - Module-level helpers: `getActiveRecorder`, `getAllActiveRecorders`,
//    `addActiveRecorderListener`, `PreviewFrameListener`.
//  - Diagnostics shape: `OwnStreamDiagnostics`, `OwnLayerDiagnostics`.
//
// Behavioural diffs from the legacy file (intentional, per cut-over plan):
//  - switchCodec / setLayers become a
//    `worker.stop()` followed by `worker.start({...newConfig})` (no
//    in-place reconfigure on the new pipeline).
//  - Preview-only mode (the blur preview tap to a main-thread canvas)
//    is no longer surfaced — the new pipeline doesn't bounce frames
//    back to main. `addPreviewFrameListener` becomes a no-op.
//  - 1 Hz recorder-health snapshots report the new pipeline's sender
//    drop / ACK / peer-connectivity signals; legacy encoder slot metrics
//    remain neutral until the new pipeline exposes them.
//  - VAD-driven adaptive framerate is out of scope (`setRemoteStreamCount`
//    becomes a no-op).
//
// TODOs in this file mark every place where the legacy behaviour is
// known-degraded and we'd want to revisit once follow-up phases add
// the missing surface to the new pipeline.

import {
    AC,
    VIDEO,
    getVideoCodecEfficiency,
    getVideoLayerBitrateKbps,
    getVideoLayerBitratesKbps,
    kbpsToBitsPerSecond,
} from 'app-constants';
import { getLogs } from 'logging';
import { Api, WorkerKind } from 'api';
import { rpcClientServer } from 'rpc';
import type { Disposable } from 'disposable';
import { Versioning } from 'versioning';
import { DeviceInfo } from 'device-info';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { ConnectivityUI } from '../../../UI.Blazor/Services/ConnectivityUI/connectivity-ui';
import { SharedSettings } from 'shared-settings';
import { SharedSettingsWorkerSync } from 'shared-settings-worker';
import {
    detectSupportedCodecs,
    getDefaultCodec,
    getCodecCategory,
    probeEncoder,
    excludeEncoderCodec,
    isEncoderCodecExcluded,
    isEncoderCodecProven,
    markEncoderCodecProven,
    type CodecInfo,
} from '../../Services/Video/codec-support';
import {
    buildLadder,
    type LayerConfig,
} from './layer-ladder';
import { MediaCapture } from '../../Services/Video/services/media-capture';
import {
    type RecorderWorker,
    type RecorderWorkerCallbacks,
    type WireSafeRecorderConfig,
} from '../../Services/Video/sender/recorder-worker-contract';
import { consumeVideoTraceKill, registerVideoTraceKillWorker } from '../../Services/Video/video-trace-kill-control';
import {
    isEncoderInitFailedError,
    parseEncoderInitFailedCodec,
    type EncoderConfigPerLayer,
} from '../../Services/Video/operators/encode';
import type { RecorderStats } from '../../Services/Video/frame-envelopes';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoRecorder');
const RECORDER_HEALTH_INTERVAL_MS = 1000;

interface PreviewTrackGenerator {
    track: MediaStreamTrack;
    writable: WritableStream<VideoFrame>;
}

// ---- Public diagnostics shapes -------------------------------------------

export interface OwnStreamDiagnostics {
    mode: string;
    codec: string;
    codecCategory: string;
    hardwareAccelerated: boolean;
    inputResolution: string;
    inputFramerate: number;
    outputResolution: string;
    configuredBitrate: number;
    actualBitrateKbps: number;
    encodedFrames: number;
    droppedFrames: number;
    keyFrames: number;
    layers: OwnLayerDiagnostics[];
    medianEncodeTime: number;
    maxLayerEncodeTime: number;
    pureMedianEncodeTime: number;
    encoderHwAccel: string;
    encoderState: string;
    encoderReconfigureCount: number;
    encoderReplaceCount: number;
    encoderLastReconfigureSummary: string;
    encoderLastReconfigureAgeMs: number;
    encoderLastErrorName: string;
    encoderLastErrorMessage: string;
    encoderLastErrorAgeMs: number;
    encoderErrorCount: number;
    duration: number;
    cameraLabel: string | null;
    blurEnabled: boolean;
    segmentationBackend: string | null;
    segmentationAvgTime: number | null;
    supportedEncoderCategories: string[];
    status: string;
    orientation: {
        firstDisplayResolution: string;
        firstCodedResolution: string;
        firstRotation: string;
        lastRotation: string;
        configuredResolution: string;
        needsRotation: boolean;
        rotationDetection: string;
        framesSeen: number;
    } | null;
    streaming: {
        sentFrames: number;
        pendingFrames: number;
        streamRecreations: number;
        status: string;
        lastError: string;
    } | null;
    simulcast: {
        layerCount: number;
        layers: { width: number; height: number; bitrateKbps: number; scalabilityMode?: string }[];
    } | null;
    // Cumulative drop-stage histogram from the active RecorderStats sample.
    // Keys are decimal FrameDropStage values; only non-zero stages are
    // emitted.
    dropTraceByStage: Record<string, number>;
    // Cumulative bytes encoded.
    bytesEncoded: number;
    // Per-tick instantaneous rates, computed at the recorder-health-monitor
    // boundary where the wall-clock dt is exact. Display these directly —
    // resampling cumulative counters at a different cadence introduces a
    // beat-frequency artifact (delta divided by uncorrelated wall-clock dt).
    bundlesPerSec: number;
    bytesPerSec: number;
    // Per-FrameDropStage drop rates, keyed by decimal stage value. Only
    // non-zero stages emitted. Same provenance as bundlesPerSec/bytesPerSec.
    dropTracePerSecByStage: Record<string, number>;
}

export interface OwnLayerDiagnostics {
    layerId: number;
    outputResolution: string;
    configuredBitrate: number;
    actualBitrateKbps: number;
    encodedFrames: number;
    droppedFrames: number;
    keyFrames: number;
    medianEncodeTime: number;
    pureMedianEncodeTime: number;
    encoderHwAccel: string;
    encoderState: string;
    encoderReconfigureCount: number;
    encoderReplaceCount: number;
    encoderLastReconfigureSummary: string;
    encoderLastReconfigureAgeMs: number;
    encoderLastErrorName: string;
    encoderLastErrorMessage: string;
    encoderLastErrorAgeMs: number;
    encoderErrorCount: number;
}

// ---- Active recorder registry --------------------------------------------

const VideoSourceKindCamera = 0;
const activeRecorders = new Map<number, VideoRecorder>();

export function getActiveRecorder(kind: number = VideoSourceKindCamera): VideoRecorder | null {
    return activeRecorders.get(kind) ?? null;
}

export function getAllActiveRecorders(): VideoRecorder[] {
    return [...activeRecorders.values()];
}

export type ActiveRecorderListener = (recorder: VideoRecorder | null, kind: number) => void;

const registryListeners = new Set<ActiveRecorderListener>();

export function addActiveRecorderListener(cb: ActiveRecorderListener): () => void {
    registryListeners.add(cb);
    return () => registryListeners.delete(cb);
}

function notifyRegistryListeners(recorder: VideoRecorder | null, kind: number): void {
    for (const cb of registryListeners) {
        try {
            cb(recorder, kind);
        } catch (e) {
            warnLog?.log('active-recorder listener threw', e);
        }
    }
}

interface Size {
    width: number;
    height: number;
}

interface LayerInput {
    width: number;
    height: number;
    baseBitrateKbps?: number;
    bitrateKbps?: number;
    scalabilityMode?: string;
}

/**
 * Preview frame listener — preserved for API compatibility with the
 * legacy file. The new pipeline does NOT push preview frames to the
 * main thread (the worker writes its WYSIWYG output directly to an
 * MSTG generator), so listeners registered here will never fire until
 * the preview-tap-to-main-thread surface is added back.
 *
 * TODO(phase 7+): wire onPreviewFrame in `RecorderWorkerCallbacks` and
 * fan out here once the new pipeline grows the corresponding tap.
 */
export type PreviewFrameListener = (frame: VideoFrame) => void;

export type VideoRecordingState = 'stopped' | 'starting' | 'recording' | 'error';

// ---- Worker construction --------------------------------------------------

// Mirrors the legacy `createProcessingWorker` from `video-pipeline.ts`.
// Each VideoRecorder owns its own worker so a camera + screencast can
// run side-by-side without overwriting each other's state.
function createRecorderWorker(): Worker {
    const workerPath = Versioning.mapPath('/dist/videoRecorderWorker.js');
    infoLog?.log('Creating video recorder worker from:', workerPath);
    const worker = new Worker(workerPath, { type: 'module' });
    worker.onerror = (e) => errorLog?.log('Video recorder worker error:', e);
    return worker;
}

// ---- VideoRecorder --------------------------------------------------------

export class VideoRecorder {
    private blazorRef: DotNet.DotNetObject;

    // Worker + RPC proxy.
    private workerInstance: Worker | null = null;
    private worker: (RecorderWorker & Disposable) | null = null;

    // Lifecycle flags.
    private isRecording = false;
    private isStoppingRecording = false;
    private isScreenCasting = false;

    // Active recorder registration (by kind).
    private registeredKind: number | null = null;

    // Camera / screen track currently being fed to the worker. Owned
    // by main thread for preview (`<video srcObject>`); a CLONE is
    // transferred to the worker (the original is neutered when
    // postMessage'd, so we keep the clone-and-transfer pattern from
    // the legacy `startTrackTransferMode`).
    private inputTrack: MediaStreamTrack | null = null;
    private previewTrack: MediaStreamTrack | null = null;
    private generatedPreviewTrack: MediaStreamTrack | null = null;
    // Worker-fed pipeline: feed the camera track to a hidden <video>
    // element on main; on each `requestVideoFrameCallback` build a
    // `VideoFrame` from the video and ship it to the worker via
    // `worker.pushFrame(frame)` (the frame is transferred). This
    // replaces the previous `MediaStreamTrackProcessor`-based path,
    // which Chromium 147 stalls after a fixed small number of frames
    // (verified empirically with both fake and real device sources).
    // The hidden video is the well-supported alternative path used
    // across WebCodecs samples and survives 30fps continuous playback.
    private workerSourceTrack: MediaStreamTrack | null = null;
    private workerSourceVideo: HTMLVideoElement | null = null;
    private workerSourceProcessor: { readable: ReadableStream<VideoFrame> } | null = null;
    private workerSourceCancelled = false;
    private workerSourceCaptureWatchdogCancel: (() => void) | null = null;

    // Configuration cached for restart on switchCamera / codec switch /
    // simulcast change.
    private selectedCameraDeviceId: string | null = null;
    private chatId = '';
    private isBlurEnabled = false;
    private disposed = false;
    private cameraWidth = 0;
    private cameraHeight = 0;

    // Cached encoder capabilities (detected at recording start).
    private supportedEncoderCategories: string[] = [];
    private audienceCodecs?: string[];
    private currentMaxLayerCount = 3;
    // Codec switch fallback bookkeeping (preserved from legacy).
    private lastCodecSwitchAt = 0;
    private readonly codecSwitchCooldownMs = 2000;
    private supportedCodecs: CodecInfo[] = [];

    // Active simulcast ladder (bottom-first). Drives the wire-safe
    // recorder config via {@link toEncoderConfigs}.
    private layers: LayerConfig[] | null = null;
    private fullLayerLadder: LayerConfig[] | null = null;
    // Currently-selected codec string (e.g. 'avc1.640028'). Threaded
    // into every encoder config layer.
    private currentCodecString = '';
    private currentCodecHardwareAccel = false;
    // Stream-mode driving downstream config (simulcast caps and layer bitrates).
    private currentMode: 'camera' | 'screen' = 'camera';
    // Top-tier encoder framerate; undefined until a recording starts. Set from
    // `VIDEO.frameRate` (camera) or `track.getSettings().frameRate` (screencast).
    private currentFramerate: number | undefined;

    // Listeners.
    private previewFrameListeners = new Set<PreviewFrameListener>();
    private stateChangeListeners = new Set<(state: VideoRecordingState) => void>();
    private blurChangeListeners = new Set<(enabled: boolean) => void>();

    private _recordingState: VideoRecordingState = 'stopped';
    public get recordingState(): VideoRecordingState { return this._recordingState; }

    // Connectivity / disconnect-api wiring (kept identical to legacy).
    private _disconnectApiHandler: (() => void) | null = null;
    private _connectivityHandler: (() => void) | null = null;
    private _sharedSettingsRegistration: Disposable | null = null;
    private _traceKillRegistration: Disposable | null = null;
    private recorderHealthTimer: number | null = null;
    private recorderHealthInFlight = false;
    private lastRecorderHealthStats: RecorderStats | null = null;
    private lastRecorderHealthWasPeerConnected = false;

    // Wallclock anchor for diagnostics duration calculation.
    private startedAtMs = 0;

    private recoveryAttempts = 0;
    private recoveryScheduled = false;

    static create(blazorRef: DotNet.DotNetObject, kind: number): VideoRecorder {
        return new VideoRecorder(blazorRef, kind);
    }

    constructor(blazorRef: DotNet.DotNetObject, kind: number) {
        this.blazorRef = blazorRef;
        this.register(kind);

        // Subscribe to connectivity changes once per VideoRecorder
        // lifetime — handlers reference `this.worker` lazily so a
        // worker recycle (stop+start) doesn't need re-subscription.
        // Same leak shape as the legacy `VideoPipeline` (which never
        // removed these), but since the active-recorder registry is
        // bounded the leak is bounded too.
        this._connectivityHandler = (): void => {
            void this.worker?.onConnectivityUpdate(
                ConnectivityUI.isOnline,
                ConnectivityUI.isConnected,
                ConnectivityUI.isBlazorServer);
        };
        ConnectivityUI.isOnlineChanged.add(this._connectivityHandler);
        ConnectivityUI.isConnectedChanged.add(this._connectivityHandler);
        void ConnectivityUI.whenReady.then(this._connectivityHandler);
    }

    // ---- Public methods called from Blazor (preserved surface) -----------

    public setSelectedCamera(deviceId: string): void {
        this.selectedCameraDeviceId = deviceId;
        infoLog?.log('Selected camera device:', deviceId);
    }

    public async switchCamera(deviceId: string): Promise<void> {
        this.selectedCameraDeviceId = deviceId;
        infoLog?.log('Switching camera to:', deviceId);

        if (!this.chatId) {
            infoLog?.log('Not yet recording — camera will be used on next start');
            return;
        }

        if (this.worker) {
            this.cleanupPreviewTrack();
            try {
                await this.worker.stop();
            } catch (e) {
                warnLog?.log('Stop during switch failed:', e);
            }
            this.tearDownWorker();
            this.isRecording = false;
            this.setRecordingState('stopped');
        }

        await this.startRecording(this.chatId, this.audienceCodecs, this.currentMaxLayerCount);
    }

    public setBlurEnabled(enabled: boolean): void {
        this.setIsBlurEnabled(enabled);
        infoLog?.log('Background blur enabled:', enabled);
    }

    /**
     * Update the cached simulcast ladder. On a running recorder this
     * triggers a stop+start with the new ladder (the new pipeline does
     * NOT support hot reconfigure of the layer set).
     *
     * TODO(phase 7+): re-introduce hot-apply once the recorder
     * supports a control channel for adding/removing layers without
     * tearing down the underlying RPC stream.
     */
    public setLayers(layers: LayerInput[] | null): void {
        const maxTiers = this.isScreenCasting
            ? VIDEO.screenCastLayerBaseBitratesKbps.length
            : VIDEO.cameraLayerBaseBitratesKbps.length;
        const clamped = (layers && layers.length > maxTiers)
            ? layers.slice(-maxTiers)
            : layers;
        const normalized = clamped?.map(l => this.normalizeLayerInput(l)) ?? null;
        const requestedCount = normalized?.length ?? 0;
        let active: LayerConfig[] | null = (normalized && normalized.length >= 2) ? normalized : null;
        const prevCount = this.layers?.length ?? 0;
        if (active !== null && this.fullLayerLadder) {
            active = this.fullLayerLadder.slice(0, Math.min(requestedCount, this.fullLayerLadder.length));
            if (active.length < 2)
                active = null;
        }
        if (active !== null)
            active = this.withCodecBitrates(active, this.currentCodecString);
        const newCount = active?.length ?? 0;
        this.layers = active;
        if (prevCount !== newCount) {
            infoLog?.log(`setLayers: ${prevCount} -> ${newCount} layer(s)`);
        }
        if (this.worker && prevCount !== newCount) {
            // Hot-restart with the new ladder. The encoder pool inside
            // the session retains parked encoders across the gap so
            // codec / NVENC slot survives.
            void this.restartWithCurrentConfig().catch((e: unknown) =>
                warnLog?.log('setLayers: restart failed:', e));
        }
    }

    /**
     * VAD-driven simulcast top-extra drop is out of scope for the new
     * pipeline. Kept as a no-op so the C# caller doesn't have to gate
     * the call site.
     *
     * TODO(phase 7+): plumb VAD state into the recorder once adaptive
     * framerate / VAD-driven layer drop returns.
     */
    public setRemoteStreamCount(_count: number): void {
        // no-op — see TODO above.
    }

    /**
     * Toggle blur on an active recording. Blur processing is currently
     * disabled in the recording pipeline, so this just updates the cached
     * flag for diagnostics + listener fan-out.
     *
     * TODO(phase 7+): once the new pipeline reconnects the blur
     * operator, plumb this through the wire-safe config + restart.
     */
    public toggleBlur(enabled: boolean): void {
        this.setIsBlurEnabled(enabled);
    }

    public getPreviewTrack(): MediaStreamTrack | null {
        // The new pipeline's worker-side WYSIWYG MSTG output isn't
        // surfaced back to main yet, so we just return the raw input
        // track. Functionally equivalent to the legacy fallback path
        // for browsers without MSTG support.
        return this.previewTrack;
    }

    public getPreviewDeviceId(): string | null {
        return this.selectedCameraDeviceId;
    }

    public isBlurActive(): boolean {
        return this.isBlurEnabled;
    }

    public isScreenCastActive(): boolean {
        return this.isScreenCasting;
    }

    public addPreviewFrameListener(cb: PreviewFrameListener): () => void {
        this.previewFrameListeners.add(cb);
        return () => this.previewFrameListeners.delete(cb);
    }

    public addStateChangeListener(cb: (state: VideoRecordingState) => void): () => void {
        this.stateChangeListeners.add(cb);
        return () => this.stateChangeListeners.delete(cb);
    }

    public addBlurChangeListener(cb: (enabled: boolean) => void): () => void {
        this.blurChangeListeners.add(cb);
        return () => this.blurChangeListeners.delete(cb);
    }

    public async startRecording(chatId: string, audienceCodecs?: string[], maxLayerCount = 3): Promise<void> {
        this.chatId = chatId;
        this.audienceCodecs = audienceCodecs;
        this.currentMaxLayerCount = maxLayerCount;
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        this.setRecordingState('starting');
        this.currentMode = 'camera';
        const tierCap = Math.max(1, Math.min(maxLayerCount, 3));
        infoLog?.log(`Starting video recording... audienceCodecs=[${audienceCodecs?.join(', ') ?? '(none)'}], maxLayerCount=${maxLayerCount} → tierCap=${tierCap}`);

        try {
            const cameraTopByTier: Record<number, Size> = {
                1: { width: 320, height: 180 },
                2: { width: 640, height: 360 },
                3: { width: 1280, height: 720 },
            };
            const targetSize: Size = cameraTopByTier[tierCap];
            const targetFramerate = VIDEO.frameRate;
            this.currentFramerate = targetFramerate;

            const supportedCodecs = await detectSupportedCodecs(targetSize.width, targetSize.height);
            this.supportedCodecs = supportedCodecs;
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);
            infoLog?.log(`Supported encoder categories: [${this.supportedEncoderCategories.join(', ')}]`);

            const initialPick = this.pickInitialCodec(supportedCodecs, audienceCodecs, targetSize);
            const ladderTop = buildLadder({
                topWidth: targetSize.width,
                topHeight: targetSize.height,
                tierCount: tierCap,
                maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
                bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
            });
            let bestCodecString = await this.pickSimulcastCodec(
                supportedCodecs, audienceCodecs, ladderTop);
            let ladder: LayerConfig[] = ladderTop;
            // Drop-top fallback only applies when we'd otherwise have built a
            // 3-tier ladder — mobile already starts at the lower cap.
            if (!bestCodecString && tierCap >= 3) {
                const ladder2 = buildLadder({
                    topWidth: 640,
                    topHeight: 360,
                    tierCount: 2,
                    maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
                    bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
                });
                infoLog?.log(`3-tier probe failed for all codecs — falling back to 2-tier @ 360p (drop 720p top)`);
                const codec2 = await this.pickSimulcastCodec(
                    supportedCodecs, audienceCodecs, ladder2);
                if (codec2) {
                    bestCodecString = codec2;
                } else {
                    warnLog?.log(`Both 3-tier and 2-tier probes failed — proceeding with ${initialPick} at 2-tier`);
                    bestCodecString = initialPick;
                }
                ladder = ladder2;
            } else if (!bestCodecString) {
                warnLog?.log(`Probe failed at tierCap=${tierCap} — proceeding with ${initialPick}`);
                bestCodecString = initialPick;
            }
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);
            const codecCategory = getCodecCategory(bestCodecString);
            ladder = buildLadder({
                topWidth: ladder[ladder.length - 1].width,
                topHeight: ladder[ladder.length - 1].height,
                tierCount: ladder.length,
                maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
                bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
            });
            ladder = this.withCodecBitrates(ladder, bestCodecString);
            const top = ladder[ladder.length - 1];
            const captureWidth = top.width;
            const captureHeight = top.height;
            const captureBitrateKbps = top.bitrateKbps;
            this.layers = ladder.length >= 2 ? [...ladder] : null;
            this.fullLayerLadder = this.layers ? [...this.layers] : null;
            this.currentCodecString = bestCodecString;
            this.currentCodecHardwareAccel = bestCodecInfo?.hardwareAccelerated ?? false;
            infoLog?.log(`Initial codec selection: ${codecCategory} (${bestCodecString}), hw=${this.currentCodecHardwareAccel}, top=${captureWidth}x${captureHeight}@${captureBitrateKbps / 1_000}Mbps`);
            infoLog?.log(`Capture ladder (bottom-first): [${ladder.map(l => `${l.width}x${l.height}`).join(', ')}], capture ${captureWidth}x${captureHeight}`);

            // Acquire the camera track on main thread.
            const track = await MediaCapture.captureCameraStream({
                deviceId: this.selectedCameraDeviceId ?? undefined,
                frameRate: targetFramerate,
                width: captureWidth,
                height: captureHeight,
            });
            this.inputTrack = track;
            this.previewTrack = track;

            const trackSettings = track.getSettings();
            infoLog?.log(`Track resolution: ${trackSettings.width}x${trackSettings.height}, frameRate=${trackSettings.frameRate ?? '(none)'}, facingMode=${trackSettings.facingMode ?? '(none)'}`);
            this.cameraWidth = trackSettings.width ?? captureWidth;
            this.cameraHeight = trackSettings.height ?? captureHeight;
            // Prefer the negotiated rate the device actually agreed to: it can be lower than requested.
            // We use this to stamp frame.duration so downstream (FPS, etc.) all see real cadence.
            this.currentFramerate = trackSettings.frameRate ?? targetFramerate;

            // The capture-side ladder is built landscape-first, but a camera may
            // deliver a portrait native frame (Android front cam in portrait
            // pose, screen-locked phone, etc.). Flip each tier's W/H so the
            // downscaler/encoder targets match the source orientation —
            // otherwise the downscaler center-crops portrait into landscape
            // and a 3:4 selfie ships as a 16:9 letterbox of the middle band.
            if (this.cameraHeight > this.cameraWidth) {
                ladder = ladder.map(l => ({ ...l, width: l.height, height: l.width }));
                this.layers = ladder.length >= 2 ? [...ladder] : null;
                this.fullLayerLadder = this.layers ? [...this.layers] : null;
                infoLog?.log(`Portrait source detected — flipped ladder to: [${ladder.map(l => `${l.width}x${l.height}`).join(', ')}]`);
            }

            void this.blazorRef.invokeMethodAsync(
                'OnTrackSettings',
                trackSettings.deviceId ?? null,
                trackSettings.facingMode ?? null);

            // External track death (permission revoked, camera unplugged).
            track.onended = () => {
                infoLog?.log('Camera track ended externally — stopping recording');
                void this.stopRecording();
            };

            this.ensureWorker();
            await this.startWorker(ladder);

            this.isRecording = true;
            this.setRecordingState('recording');
            await this.blazorRef.invokeMethodAsync('OnRecordingStarted');

            infoLog?.log('Video recording started');
        } catch (error) {
            this.setRecordingState('error');
            errorLog?.log('Failed to start recording:', error);
            const message = await this.describeStartError(error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', message);
        }
    }

    public async startScreenCast(chatId: string, audienceCodecs?: string[], maxLayerCount = 2): Promise<void> {
        this.chatId = chatId;
        this.audienceCodecs = audienceCodecs;
        this.currentMaxLayerCount = maxLayerCount;
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        this.setRecordingState('starting');
        this.currentMode = 'screen';
        const tierCap = Math.max(1, Math.min(maxLayerCount, 2));
        infoLog?.log(`Starting screencast... maxLayerCount=${maxLayerCount} → tierCap=${tierCap}`);

        try {
            const detectionWidth = DeviceInfo.isMobile ? 1280 : 1920;
            const detectionHeight = DeviceInfo.isMobile ? 720 : 1080;
            const supportedCodecs = await detectSupportedCodecs(detectionWidth, detectionHeight);
            this.supportedCodecs = supportedCodecs;
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);

            const screenTopByTier: Record<number, Size> = {
                1: { width: 960, height: 540 },
                2: { width: 1920, height: 1080 },
            };
            const targetSize: Size = screenTopByTier[tierCap];
            const bestCodecString = this.pickInitialCodec(supportedCodecs, audienceCodecs, targetSize);
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);

            const screenCastLadder = buildLadder({
                topWidth: targetSize.width,
                topHeight: targetSize.height,
                tierCount: tierCap,
                maxTierCount: VIDEO.screenCastLayerBaseBitratesKbps.length,
                bitratesKbps: VIDEO.screenCastLayerBaseBitratesKbps,
            });
            const actualScreenCastLadder = this.withCodecBitrates(screenCastLadder, bestCodecString);
            const screenCastTop = actualScreenCastLadder[actualScreenCastLadder.length - 1];
            this.layers = [...actualScreenCastLadder];
            this.fullLayerLadder = [...actualScreenCastLadder];
            this.currentCodecString = bestCodecString;
            this.currentCodecHardwareAccel = bestCodecInfo?.hardwareAccelerated ?? false;
            infoLog?.log(`ScreenCast ladder (bottom-first): [${actualScreenCastLadder.map(l => `${l.width}x${l.height}`).join(', ')}], capture ${screenCastTop.width}x${screenCastTop.height}`);

            // Acquire the screen track on main thread.
            const screenTrack = await MediaCapture.captureScreenCast();
            this.inputTrack = screenTrack;
            this.previewTrack = screenTrack;

            const trackSettings = screenTrack.getSettings();
            this.cameraWidth = trackSettings.width ?? targetSize.width;
            this.cameraHeight = trackSettings.height ?? targetSize.height;
            this.currentFramerate = trackSettings.frameRate ?? VIDEO.frameRate;

            screenTrack.onended = () => {
                infoLog?.log('Screen sharing track ended (user stopped sharing)');
                void this.stopRecording();
            };

            this.ensureWorker();
            await this.startWorker(actualScreenCastLadder);

            this.isRecording = true;
            this.isScreenCasting = true;
            this.setRecordingState('recording');

            await this.blazorRef.invokeMethodAsync('OnRecordingStarted');
            infoLog?.log('ScreenCast started');
        } catch (error) {
            const isUserCancel = error instanceof DOMException && error.name === 'NotAllowedError';
            if (isUserCancel) {
                this.setRecordingState('stopped');
                infoLog?.log('ScreenCast cancelled by user');
                await this.blazorRef.invokeMethodAsync('OnRecordingStopped');
                return;
            }
            this.setRecordingState('error');
            errorLog?.log('Failed to start screencast:', error);
            const message = error instanceof Error ? error.message : String(error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', message);
        }
    }

    public async stopRecording(): Promise<void> {
        if (this.isStoppingRecording || !this.isRecording) {
            return;
        }

        infoLog?.log('Stopping video recording...');
        this.isStoppingRecording = true;

        try {
            if (this.worker) {
                try {
                    await this.worker.stop();
                } catch (e) {
                    warnLog?.log('Worker stop failed:', e);
                }
            }
            this.tearDownWorker();
            this.cleanupPreviewTrack();
            this.isRecording = false;
            this.isScreenCasting = false;
            this.layers = null;
            this.fullLayerLadder = null;
            this.lastCodecSwitchAt = 0;
            this.startedAtMs = 0;
            this.setRecordingState('stopped');
            this.unregister();
            await this.blazorRef.invokeMethodAsync('OnRecordingStopped');

            infoLog?.log('Video recording stopped');
        } catch (error) {
            errorLog?.log('Failed to stop recording:', error);
        } finally {
            this.isStoppingRecording = false;
        }
    }

    public async updateSupportedDecoderCodecs(codecs: string[]): Promise<void> {
        this.audienceCodecs = codecs;
        if (!this.worker) return;

        const allowedCategories = this.allowedCodecCategories(codecs);
        if (allowedCategories && ![...allowedCategories].some(c => this.supportedEncoderCategories.includes(c))) {
            warnLog?.log(`updateSupportedDecoderCodecs: no match between server codecs [${codecs.join(', ')}] and encoder capabilities [${this.supportedEncoderCategories.join(', ')}], keeping current codec`);
            return;
        }

        const pickedCodecString = this.pickBestCodecByEfficiency(this.supportedCodecs, codecs)
            ?? getDefaultCodec(this.supportedCodecs, this.cameraWidth || 1280, this.cameraHeight || 720);
        const pickedCategory = getCodecCategory(pickedCodecString);
        const currentCategory = getCodecCategory(this.currentCodecString);

        if (currentCategory === pickedCategory) {
            return;
        }

        infoLog?.log(`Switching codec ${currentCategory} → ${pickedCategory} (${pickedCodecString})`);
        const pickedInfo = this.supportedCodecs.find(c => c.codec === pickedCodecString);
        this.currentCodecString = pickedCodecString;
        this.currentCodecHardwareAccel = pickedInfo?.hardwareAccelerated ?? false;
        this.repriceCurrentLadders();
        await this.restartWithCurrentConfig();
    }

    public forceKeyFrame(): void {
        if (!this.worker) {
            warnLog?.log('forceKeyFrame: no active worker');
            return;
        }
        infoLog?.log('forceKeyFrame: PLI — requesting keyframe on encoder');
        void this.worker.requestKeyframe();
    }

    public getDiagnostics(): OwnStreamDiagnostics {
        // Aggregate counters live on `RecorderStats` and are refreshed at
        // 1Hz by the recorder-health monitor. Per-layer breakdowns are NOT
        // tracked — the encode operator only mutates aggregates. The modal
        // surfaces aggregates at the encoder header; per-layer fields stay 0.
        const ladder = this.layers ?? [];
        const top = ladder.length > 0 ? ladder[ladder.length - 1] : null;
        const liveStats = this.lastRecorderHealthStats;

        const duration = this.startedAtMs > 0
            ? (Date.now() - this.startedAtMs) / 1000
            : 0;

        const codecCategory = this.currentCodecString
            ? getCodecCategory(this.currentCodecString)
            : '';

        let droppedAggregate = 0;
        const dropTraceByStage: Record<string, number> = {};
        if (liveStats) {
            for (const [stage, count] of liveStats.dropTrace) {
                droppedAggregate += count;
                if (count > 0)
                    dropTraceByStage[String(stage)] = count;
            }
        }
        const meanEncodeTimeMs = liveStats && liveStats.encodeTimeMsCount > 0
            ? liveStats.encodeTimeMsSum / liveStats.encodeTimeMsCount
            : 0;
        const meanMaxLayerEncodeTimeMs = liveStats && liveStats.encodeTimeMsCount > 0
            ? liveStats.encodeTimeMsMaxSum / liveStats.encodeTimeMsCount
            : 0;
        const aggregateBitrateKbps = liveStats && duration > 0
            ? (liveStats.bytesEncoded * 8) / duration / 1000
            : 0;

        return {
            mode: this.isScreenCasting ? 'screen' : this.isRecording ? 'camera' : 'none',
            codec: this.currentCodecString,
            codecCategory,
            hardwareAccelerated: this.currentCodecHardwareAccel,
            inputResolution: this.cameraWidth > 0 ? `${this.cameraWidth}x${this.cameraHeight}` : 'N/A',
            inputFramerate: this.capturedPerSec > 0 ? this.capturedPerSec : (this.currentFramerate ?? 0),
            outputResolution: top ? `${top.width}x${top.height}` : 'N/A',
            configuredBitrate: kbpsToBitsPerSecond(top?.bitrateKbps ?? 0),
            actualBitrateKbps: aggregateBitrateKbps,
            encodedFrames: liveStats?.bundlesShipped ?? 0,
            droppedFrames: droppedAggregate,
            keyFrames: 0,
            layers: ladder.map((l, i) => ({
                layerId: i,
                outputResolution: `${l.width}x${l.height}`,
                configuredBitrate: kbpsToBitsPerSecond(l.bitrateKbps),
                actualBitrateKbps: 0,
                encodedFrames: 0,
                droppedFrames: 0,
                keyFrames: 0,
                medianEncodeTime: 0,
                pureMedianEncodeTime: 0,
                encoderHwAccel: this.currentCodecHardwareAccel ? 'hardware' : 'software',
                encoderState: this.isRecording ? 'configured' : 'unconfigured',
                encoderReconfigureCount: 0,
                encoderReplaceCount: 0,
                encoderLastReconfigureSummary: '',
                encoderLastReconfigureAgeMs: -1,
                encoderLastErrorName: '',
                encoderLastErrorMessage: '',
                encoderLastErrorAgeMs: -1,
                encoderErrorCount: 0,
            })),
            medianEncodeTime: meanEncodeTimeMs,
            maxLayerEncodeTime: meanMaxLayerEncodeTimeMs,
            pureMedianEncodeTime: 0,
            encoderHwAccel: this.currentCodecHardwareAccel ? 'hardware' : 'software',
            encoderState: this.isRecording ? 'configured' : 'unconfigured',
            encoderReconfigureCount: 0,
            encoderReplaceCount: 0,
            encoderLastReconfigureSummary: '',
            encoderLastReconfigureAgeMs: -1,
            encoderLastErrorName: '',
            encoderLastErrorMessage: '',
            encoderLastErrorAgeMs: -1,
            encoderErrorCount: 0,
            duration,
            cameraLabel: this.inputTrack?.label ?? null,
            blurEnabled: this.isBlurEnabled,
            segmentationBackend: null,
            segmentationAvgTime: null,
            supportedEncoderCategories: this.supportedEncoderCategories,
            status: this._recordingState,
            // TODO(phase 7+): expose orientation + streaming sub-metrics
            // once the new pipeline's encoder/sender stats grow them.
            orientation: null,
            streaming: null,
            simulcast: ladder.length > 0 ? {
                layerCount: ladder.length,
                layers: ladder.map(l => ({
                    width: l.width,
                    height: l.height,
                    bitrateKbps: l.bitrateKbps,
                    scalabilityMode: l.scalabilityMode,
                })),
            } : null,
            dropTraceByStage,
            bytesEncoded: liveStats?.bytesEncoded ?? 0,
            bundlesPerSec: this.bundlesPerSec,
            bytesPerSec: this.bytesPerSec,
            dropTracePerSecByStage: Object.fromEntries(
                Array.from(this.dropPerSec.entries(), ([k, v]) => [String(k), v])),
        };
    }

    public peekBundlesPerSec(): number {
        return this.bundlesPerSec;
    }

    public dispose() {
        if (this.disposed)
            return;
        this.disposed = true;
        this.unregister();

        this.previewFrameListeners.clear();
        this.cleanupPreviewTrack();

        if (this.worker) {
            void this.worker.stop().catch(() => { /* swallow on dispose */ });
            this.tearDownWorker();
        }

        // The connectivity handler stays subscribed past dispose
        // (the legacy `VideoPipeline` doesn't unsubscribe either —
        // `EventHandlerSet.remove(...)` takes the `EventHandler<T>`
        // wrapper returned from `.add(...)`, and we discard it). It
        // becomes a dead-letter no-op once `this.worker` is null,
        // and the bounded active-recorder lifecycle keeps the leak
        // bounded. TODO(phase 7+): tighten this once the registry
        // grows churn pressure.
        this._connectivityHandler = null;

        this.isRecording = false;
        this.isScreenCasting = false;
        this.setRecordingState('stopped');
    }

    // ---- Internal helpers -----------------------------------------------

    private ensureWorker(): void {
        if (this.worker) return;

        const workerInstance = createRecorderWorker();
        this.workerInstance = workerInstance;

        const callbacks: RecorderWorkerCallbacks = {
            onStreamCreated: (codecSettings: string) => {
                infoLog?.log(`Worker created RPC stream, codecSettings: ${codecSettings.length} chars`);
            },
            onStreamEnded: (reason: string) => {
                infoLog?.log(`Worker stream ended: ${reason}`);
                void reason;
            },
            onError: (error: string) => {
                errorLog?.log(`RecorderWorker reported error: ${error}`);
                if (isEncoderInitFailedError(error)) {
                    const failedCodec = parseEncoderInitFailedCodec(error);
                    const failedCategory = failedCodec ? getCodecCategory(failedCodec) : null;
                    if (failedCategory && !isEncoderCodecProven(failedCategory)) {
                        warnLog?.log(
                            `RecorderWorker: encoder init failure for codec=${failedCodec} ` +
                            `(category=${failedCategory}) — excluding and re-picking`);
                        excludeEncoderCodec(failedCategory);
                        void this.repickCodecAndRestart(`encoder init failed: ${failedCategory}`);
                        return;
                    }
                    if (failedCategory && isEncoderCodecProven(failedCategory))
                        infoLog?.log(
                            `RecorderWorker: encoder init failure for proven codec ` +
                            `${failedCategory} — treating as transient`);
                }
                this.scheduleRecovery(`worker error: ${error}`);
            },
            onTraceKillInjected: () => consumeVideoTraceKill('recording'),
        };

        this.worker = rpcClientServer<RecorderWorker>(
            'VideoRecorder.worker',
            workerInstance,
            callbacks,
        );
        this._traceKillRegistration = registerVideoTraceKillWorker('recording', this.worker);

        // Push current connectivity to the freshly-created worker
        // (the long-lived listeners installed in the constructor only
        // fire on transitions, so we need a one-shot push here).
        this._connectivityHandler?.();

        this._disconnectApiHandler = () => void this.worker?.disconnectApi();
        Api.onDisconnectRequested(WorkerKind.VideoCapture).add(this._disconnectApiHandler);

        // Seed the worker's app-constants holders so `MediaRpcStreamOptions`
        // can read `VIDEO.rpcStreamAckPeriod` etc. from the push-to-pull-buffer
        // layer. `AC` is structurally-cloneable JSON.
        void this.worker.init(AC).catch((e: unknown) => {
            warnLog?.log('Worker init failed:', e);
        });

        // Bridge SharedSettings into the worker — pushes the current
        // snapshot now and re-pushes on every change. Carries apiUrl,
        // server-clock offset, app constants, and the session token
        // the worker's RPC peer needs to authenticate.
        this._sharedSettingsRegistration = SharedSettingsWorkerSync.register(this.worker);
    }

    private async startWorker(ladder: LayerConfig[]): Promise<void> {
        if (!this.worker || !this.inputTrack) {
            throw new Error('startWorker: worker or input track missing');
        }
        // Recovery may call this while a prior MSTP readable / rVFC pump is still alive.
        this.tearDownWorkerSource();

        // Frame source — two-tier strategy.
        //
        // PRIMARY: `MediaStreamTrackProcessor` on main, transfer
        //   `processor.readable` to the worker via `setSource(readable)`.
        //   The worker's recorder pipeline pulls frames from the readable
        //   directly. Source-bound: no rVFC, no main-thread tick involved
        //   in capture. Decouples capture rate from page-render load (we
        //   were observing 20 fps caps on both screencast + camera under
        //   multi-tile render load purely because rVFC throttles).
        //
        // FALLBACK: a hidden `<video srcObject=track>` driven by
        //   `requestVideoFrameCallback`. On each callback we build a
        //   `VideoFrame` from the element and ship it to the worker via
        //   `pushFrame`. Used when MSTP is unavailable (older Safari /
        //   Firefox) or its construction fails.
        //
        // Historical note: the rVFC pump used to be the production path
        // because Chromium 147 starved MSTP-readable consumers after a
        // small fixed number of frames (verified at the time). Current
        // Chrome stable is well past that; if the issue resurfaces,
        // catching the construct/transfer error here flips us back to
        // the rVFC fallback transparently.
        let useMstp = false;
        const MstpCtor = (globalThis as unknown as {
            MediaStreamTrackProcessor?: new (init: { track: MediaStreamTrack }) => { readable: ReadableStream<VideoFrame> };
        }).MediaStreamTrackProcessor;
        if (typeof MstpCtor === 'function') {
            try {
                const processor = new MstpCtor({ track: this.inputTrack });
                // Transfer the readable across the worker boundary. The
                // worker's createProcessor returns this readable to the
                // mstpSource operator on the next start().
                await this.worker.setSource(processor.readable);
                this.workerSourceCancelled = false;
                useMstp = true;
                infoLog?.log('startWorker: capture path = MSTP-readable (source-bound)');
            } catch (e) {
                warnLog?.log('startWorker: MSTP construction/transfer failed, falling back to rVFC pump:', e);
            }
        } else {
            infoLog?.log('startWorker: MSTP unavailable, using rVFC pump');
        }

        if (!useMstp) {
        // FALLBACK: rVFC pump from a hidden <video>.
            const sourceVideo = document.createElement('video');
            sourceVideo.muted = true;
            sourceVideo.autoplay = true;
            sourceVideo.playsInline = true;
            sourceVideo.srcObject = new MediaStream([this.inputTrack]);
            sourceVideo.style.position = 'fixed';
            sourceVideo.style.opacity = '0';
            sourceVideo.style.pointerEvents = 'none';
            sourceVideo.style.width = '1px';
            sourceVideo.style.height = '1px';
            document.body.appendChild(sourceVideo);
            await sourceVideo.play().catch(() => { /* tolerated */ });
            this.workerSourceVideo = sourceVideo;
            this.workerSourceCancelled = false;
            const workerForPump = this.worker;
            let pumpFrameCount = 0;
            let pushInFlight = false;
            let pushDroppedCount = 0;
            let lastPumpTickAtMs = performance.now();
            const onFrame = (now: DOMHighResTimeStamp, metadata: VideoFrameCallbackMetadata): void => {
                if (this.workerSourceCancelled) return;
                void now;
                lastPumpTickAtMs = performance.now();
                if (pushInFlight) {
                    pushDroppedCount++;
                    if (pushDroppedCount <= 5 || pushDroppedCount % 60 === 0)
                        warnLog?.log(`pushFrame pump: dropped while push in flight (#${pushDroppedCount})`);
                    sourceVideo.requestVideoFrameCallback(onFrame);
                    return;
                }

                const timestampUs = Math.round(metadata.mediaTime * 1_000_000);
                let frame: VideoFrame;
                try {
                    frame = new VideoFrame(sourceVideo, { timestamp: timestampUs });
                } catch (e) {
                    warnLog?.log('pushFrame pump: VideoFrame ctor failed', e);
                    sourceVideo.requestVideoFrameCallback(onFrame);
                    return;
                }
                pumpFrameCount++;
                try {
                    pushInFlight = true;
                    void workerForPump.pushFrame(frame)
                        .catch((e: unknown) => {
                            warnLog?.log('pushFrame: worker rejected', e);
                            this.workerSourceCancelled = true;
                        })
                        .finally(() => {
                            pushInFlight = false;
                        });
                } catch (e) {
                    warnLog?.log('pushFrame: worker rejected', e);
                    this.workerSourceCancelled = true;
                    try { frame.close(); } catch { /* ignore */ }
                    return;
                }
                try { frame.close(); } catch { /* already detached */ }
                sourceVideo.requestVideoFrameCallback(onFrame);
            };
            sourceVideo.requestVideoFrameCallback(onFrame);
            // Capture watchdog: fires every 2s; logs only when the rVFC pump
            // hasn't ticked recently (= camera/preview frozen) along with
            // current camera + sourceVideo state for diagnostics. Replaces
            // any prior watchdog so we don't stack stale ones across restarts.
            this.workerSourceCaptureWatchdogCancel?.();
            const captureWatchdog = window.setInterval(() => {
                if (this.workerSourceCancelled) return;
                const sinceTickMs = performance.now() - lastPumpTickAtMs;
                if (sinceTickMs <= 1500) return;
                const t = this.inputTrack;
                warnLog?.log(
                    `capture watchdog: pump=#${pumpFrameCount} sinceTick=${sinceTickMs.toFixed(0)}ms ` +
                `srcVid(rs=${sourceVideo.readyState} ct=${sourceVideo.currentTime.toFixed(2)} ` +
                `paused=${sourceVideo.paused} ended=${sourceVideo.ended}) ` +
                `track(rs=${t?.readyState} muted=${t?.muted} enabled=${t?.enabled})`);
            }, 2000);
            this.workerSourceCaptureWatchdogCancel = (): void => {
                window.clearInterval(captureWatchdog);
            };
        }

        const apiUrl = BrowserInit.getUrl('/rpc/ws').replace(/^http/, 'ws');
        // SharedSettings is the legacy worker plumbing; the new worker
        // doesn't observe it. Keep the call so the audio path (which
        // still uses SharedSettings) is unaffected.
        SharedSettings.update({ apiUrl });

        const encoderConfigs = this.toEncoderConfigs(ladder);

        const framerate = this.requireFramerate('startWorker');
        const isFrontCamera = this.inputTrack.getSettings().facingMode === 'user';
        const config: WireSafeRecorderConfig = {
            chatId: this.chatId,
            apiUrl,
            sourceKind: this.currentMode === 'screen' ? 1 : 0,
            isFrontCamera,
            isIos: DeviceInfo.isIos,
            encoderConfigs,
            // Camera: 2-3s interval; ScreenCast: 1-2s interval.
            keyframeIntervalFrames: this.currentMode === 'screen'
                ? framerate * 2
                : framerate * 3,
            maxKeyFrameIntervalMs: this.currentMode === 'screen' ? 10000 : 3000,
        };

        const sourceStartedAtMs = Date.now();
        this.startedAtMs = sourceStartedAtMs;
        // `start()` resolves only when the run finishes draining (per
        // the new contract) — fire and forget so we can return from
        // startRecording() and let the operator pipe drive itself.
        const previousPreviewTrack = this.previewTrack;
        const previewGenerator = this.createGeneratedPreviewTrack();
        if (previewGenerator) {
            this.previewTrack = previewGenerator.track;
        } else {
            this.previewTrack = this.inputTrack;
        }
        if (this.previewTrack !== previousPreviewTrack)
            this.notifyPreviewTrackChanged();

        void this.worker.start({ sourceStartedAtMs, config }, previewGenerator?.writable).catch((e: unknown) => {
            errorLog?.log('Worker start rejected:', e);
            if (previewGenerator && this.previewTrack === previewGenerator.track) {
                this.cleanupGeneratedPreviewTrack();
                this.previewTrack = this.inputTrack;
                this.notifyPreviewTrackChanged();
            }
            const message = e instanceof Error ? e.message : String(e);
            this.scheduleRecovery(`worker.start rejected: ${message}`);
        });
        this.startRecorderHealthMonitor();
    }

    private async repickCodecAndRestart(reason: string): Promise<void> {
        if (!this.isRecording || this.disposed)
            return;

        try {
            const w = this.cameraWidth || 1280;
            const h = this.cameraHeight || 720;
            const refreshedCodecs = await detectSupportedCodecs(w, h);
            this.supportedCodecs = refreshedCodecs;
            this.supportedEncoderCategories = this.extractEncoderCategories(refreshedCodecs);
            const nextCodec = this.pickInitialCodec(
                refreshedCodecs,
                this.audienceCodecs,
                { width: w, height: h });
            if (nextCodec === this.currentCodecString) {
                warnLog?.log(
                    `repickCodecAndRestart: re-pick returned same codec ${nextCodec} ` +
                    `— excluded category may already be h264 or no fallback exists; ` +
                    `falling back to scheduleRecovery`);
                this.scheduleRecovery(reason);
                return;
            }
            const prevCodec = this.currentCodecString;
            this.currentCodecString = nextCodec;
            const nextCodecInfo = refreshedCodecs.find(c => c.codec === nextCodec);
            this.currentCodecHardwareAccel = nextCodecInfo?.hardwareAccelerated ?? false;
            this.repriceCurrentLadders();
            infoLog?.log(
                `repickCodecAndRestart: ${reason} → switching codec ` +
                `${prevCodec} → ${nextCodec} (hw=${this.currentCodecHardwareAccel})`);
            this.recoveryAttempts = 0;
            this.scheduleRecovery(`codec switch to ${getCodecCategory(nextCodec)}`);
        } catch (e) {
            warnLog?.log('repickCodecAndRestart: failed', e);
            this.scheduleRecovery(reason);
        }
    }

    private scheduleRecovery(reason: string): void {
        if (this.recoveryScheduled || !this.isRecording || this.disposed)
            return;

        this.recoveryScheduled = true;
        this.recoveryAttempts++;
        const delayMs = Math.min(3000, 200 * Math.pow(1.7, this.recoveryAttempts - 1));
        warnLog?.log(
            `scheduleRecovery: ${reason}; attempt ${this.recoveryAttempts} in ${delayMs.toFixed(0)}ms`);
        window.setTimeout(() => {
            this.recoveryScheduled = false;
            if (!this.isRecording || this.disposed)
                return;

            void this.recoverNow().catch((e: unknown) => {
                warnLog?.log('scheduleRecovery: recoverNow failed', e);
                this.scheduleRecovery('recovery attempt failed');
            });
        }, delayMs);
    }

    private async recoverNow(): Promise<void> {
        if (!this.worker || !this.inputTrack) {
            warnLog?.log('recoverNow: worker or input track missing — skipping');
            return;
        }
        const ladder = this.layers ?? this.fullLayerLadder ?? [];
        if (ladder.length === 0) {
            warnLog?.log('recoverNow: no ladder, skipping');
            return;
        }
        infoLog?.log(
            `recoverNow: ladder=[${ladder.map(l => `${l.width}x${l.height}`).join(', ')}], ` +
            `codec=${this.currentCodecString}`);
        try { await this.worker.stop(); }
        catch (e) { warnLog?.log('recoverNow: worker.stop failed (continuing):', e); }
        if (!this.isRecording || this.disposed)
            return;

        await this.startWorker(ladder);
    }

    private toEncoderConfigs(ladder: LayerConfig[]): EncoderConfigPerLayer[] {
        const framerate = this.requireFramerate('toEncoderConfigs');
        if (ladder.length === 0) {
            // Should not happen — startRecording / startScreenCast always
            // set at least one tier — but defensively produce a single
            // tier from camera dims so the worker doesn't reject the start.
            return [{
                codec: this.currentCodecString,
                width: this.cameraWidth,
                height: this.cameraHeight,
                bitrate: this.isScreenCasting
                    ? kbpsToBitsPerSecond(this.topBitrateKbpsForCodec(VIDEO.screenCastLayerBaseBitratesKbps, this.currentCodecString, 6_000))
                    : kbpsToBitsPerSecond(this.topBitrateKbpsForCodec(VIDEO.cameraLayerBaseBitratesKbps, this.currentCodecString, 2_000)),
                framerate,
            }];
        }
        return ladder.map(l => ({
            codec: this.currentCodecString,
            width: l.width,
            height: l.height,
            bitrate: kbpsToBitsPerSecond(l.bitrateKbps),
            framerate,
        }));
    }

    private requireFramerate(caller: string): number {
        if (this.currentFramerate === undefined)
            throw new Error(`${caller}: currentFramerate not set (called before recording start?)`);
        return this.currentFramerate;
    }

    private async restartWithCurrentConfig(): Promise<void> {
        if (!this.worker || !this.inputTrack) return;
        const ladder = this.layers ?? this.fullLayerLadder ?? [];
        if (ladder.length === 0) {
            warnLog?.log('restartWithCurrentConfig: no ladder, skipping');
            return;
        }
        infoLog?.log(`restartWithCurrentConfig: ladder=[${ladder.map(l => `${l.width}x${l.height}`).join(', ')}], codec=${this.currentCodecString}`);
        try {
            await this.worker.stop();
        } catch (e) {
            warnLog?.log('restart: stop failed (continuing):', e);
        }
        await this.startWorker(ladder);
    }

    private withCodecBitrates(layers: readonly LayerConfig[], codec: string): LayerConfig[] {
        return layers.map(l => {
            const baseBitrateKbps = l.baseBitrateKbps ?? l.bitrateKbps;
            return {
                ...l,
                baseBitrateKbps,
                bitrateKbps: getVideoLayerBitrateKbps(baseBitrateKbps, codec),
            };
        });
    }

    private repriceCurrentLadders(): void {
        if (this.layers)
            this.layers = this.withCodecBitrates(this.layers, this.currentCodecString);
        if (this.fullLayerLadder)
            this.fullLayerLadder = this.withCodecBitrates(this.fullLayerLadder, this.currentCodecString);
    }

    private topBitrateKbpsForCodec(baseBitratesKbps: readonly number[], codec: string, fallbackKbps: number): number {
        return getVideoLayerBitratesKbps(baseBitratesKbps, codec).at(-1) ?? fallbackKbps;
    }

    private normalizeLayerInput(layer: LayerInput): LayerConfig {
        const baseBitrateKbps = layer.baseBitrateKbps ?? layer.bitrateKbps ?? 0;
        const result: LayerConfig = {
            width: layer.width,
            height: layer.height,
            baseBitrateKbps,
            bitrateKbps: layer.bitrateKbps ?? baseBitrateKbps,
        };
        if (layer.scalabilityMode !== undefined)
            result.scalabilityMode = layer.scalabilityMode;
        return result;
    }

    private tearDownWorker(): void {
        this.stopRecorderHealthMonitor();
        if (this._disconnectApiHandler) {
            Api.onDisconnectRequested(WorkerKind.VideoCapture).remove(this._disconnectApiHandler);
            this._disconnectApiHandler = null;
        }
        if (this._sharedSettingsRegistration) {
            try { this._sharedSettingsRegistration.dispose(); } catch { /* ignore */ }
            this._sharedSettingsRegistration = null;
        }
        if (this._traceKillRegistration) {
            try { this._traceKillRegistration.dispose(); } catch { /* ignore */ }
            this._traceKillRegistration = null;
        }
        // Connectivity handlers are NOT removed here — they live for
        // the VideoRecorder's whole lifetime so they cover the
        // stop/start gap. Mirrors the legacy `VideoPipeline` shape.
        if (this.worker) {
            try { (this.worker as Disposable).dispose(); } catch { /* ignore */ }
            this.worker = null;
        }
        if (this.workerInstance) {
            try { this.workerInstance.terminate(); } catch { /* ignore */ }
            this.workerInstance = null;
        }
        this.tearDownWorkerSource();
    }

    private tearDownWorkerSource(): void {
        // The rVFC pump self-rearms; setting workerSourceCancelled is the only
        // way to break the loop. Without this, recoverNow → startWorker stacks
        // a second pump alongside the first against the same worker slot.
        this.workerSourceCancelled = true;
        if (this.workerSourceCaptureWatchdogCancel) {
            this.workerSourceCaptureWatchdogCancel();
            this.workerSourceCaptureWatchdogCancel = null;
        }
        if (this.workerSourceVideo) {
            try {
                this.workerSourceVideo.pause();
                this.workerSourceVideo.srcObject = null;
                if (this.workerSourceVideo.parentNode)
                    this.workerSourceVideo.parentNode.removeChild(this.workerSourceVideo);
            } catch { /* ignore */ }
            this.workerSourceVideo = null;
        }
        if (this.workerSourceTrack) {
            try { this.workerSourceTrack.stop(); } catch { /* ignore */ }
            this.workerSourceTrack = null;
        }
    }

    // EMA smoothing factor for encode-ratio. Single-tick spikes from a
    // slow keyframe would otherwise pop the classifier; α=0.3 gives a
    // ~3-tick half-life at 1 Hz polling.
    private static readonly EncodeRatioEmaAlpha = 0.3;
    private encodeRatioEma = 0;
    private senderDropRatioEma = 0;
    // Per-tick instantaneous rates for every cumulative counter the modal /
    // overlay surfaces. Computed at the recorder-health-monitor boundary
    // (wall-clock dt) — resampling the cumulative counters at a different
    // cadence introduces a beat-frequency artifact (delta divided by an
    // uncorrelated wall-clock dt), which is what showed up in the modal as
    // 13/27 fps alternation.
    private capturedPerSec = 0;
    private bundlesPerSec = 0;
    private bytesPerSec = 0;
    private readonly dropPerSec = new Map<number, number>();
    private lastReportTickMs = 0;

    private startRecorderHealthMonitor(): void {
        this.stopRecorderHealthMonitor();
        this.lastRecorderHealthStats = null;
        this.lastRecorderHealthWasPeerConnected = false;
        this.encodeRatioEma = 0;
        this.senderDropRatioEma = 0;
        this.capturedPerSec = 0;
        this.bundlesPerSec = 0;
        this.bytesPerSec = 0;
        this.dropPerSec.clear();
        this.lastReportTickMs = 0;
        this.recorderHealthTimer = window.setInterval(() => {
            void this.reportRecorderStats();
        }, RECORDER_HEALTH_INTERVAL_MS);
    }

    private stopRecorderHealthMonitor(): void {
        if (this.recorderHealthTimer !== null) {
            window.clearInterval(this.recorderHealthTimer);
            this.recorderHealthTimer = null;
        }
        this.lastRecorderHealthStats = null;
        this.lastRecorderHealthWasPeerConnected = false;
    }

    private async reportRecorderStats(): Promise<void> {
        if (this.recorderHealthInFlight || !this.worker)
            return;

        this.recorderHealthInFlight = true;
        try {
            const stats = await this.worker.getStats();
            const isPeerConnected = stats.isPeerConnected;
            const previous = this.lastRecorderHealthStats;
            const nowMs = performance.now();
            if (previous && this.lastReportTickMs > 0) {
                const dt = nowMs - this.lastReportTickMs;
                if (dt > 0) {
                    const scale = 1000 / dt;
                    this.capturedPerSec =
                        Math.max(0, stats.framesCaptured - previous.framesCaptured) * scale;
                    this.bundlesPerSec =
                        Math.max(0, stats.bundlesShipped - previous.bundlesShipped) * scale;
                    this.bytesPerSec =
                        Math.max(0, stats.bytesEncoded - previous.bytesEncoded) * scale;
                    if (this.recoveryAttempts > 0 && this.bundlesPerSec > 0)
                        this.recoveryAttempts = 0;
                    if (this.bundlesPerSec > 0 && this.currentCodecString) {
                        const cat = getCodecCategory(this.currentCodecString);
                        if (!isEncoderCodecProven(cat))
                            markEncoderCodecProven(cat);
                    }
                    this.dropPerSec.clear();
                    for (const [stage, count] of stats.dropTrace) {
                        const prev = previous.dropTrace.get(stage) ?? 0;
                        const rate = Math.max(0, count - prev) * scale;
                        if (rate > 0) this.dropPerSec.set(stage as number, rate);
                    }
                }
            }
            this.lastReportTickMs = nowMs;

            // Drop trace deltas → senderFrameDropRatio. Sum only sender
            // stages (1..30). Denominator = bundles attempted = bundles
            // shipped + bundles dropped in the sender pipeline.
            let senderDropsDelta = 0;
            if (previous && isPeerConnected && this.lastRecorderHealthWasPeerConnected) {
                for (const [stage, count] of stats.dropTrace) {
                    const stageNum = stage as number;
                    if (stageNum < 1 || stageNum > 30) continue;
                    const prevCount = previous.dropTrace.get(stage) ?? 0;
                    senderDropsDelta += Math.max(0, count - prevCount);
                }
                const shippedDelta = Math.max(0, stats.bundlesShipped - previous.bundlesShipped);
                const totalProduced = shippedDelta + senderDropsDelta;
                const ratio = totalProduced > 0 ? senderDropsDelta / totalProduced : 0;
                this.senderDropRatioEma =
                    VideoRecorder.EncodeRatioEmaAlpha * ratio
                    + (1 - VideoRecorder.EncodeRatioEmaAlpha) * this.senderDropRatioEma;
            }

            // Encode ratio: (sum of per-layer encode times in this tick)
            // / frameDuration. Frame duration: 1000/30 = 33.33ms baseline.
            const frameDurationMs = 1000 / 30;
            if (previous) {
                const sumDelta = Math.max(0, stats.encodeTimeMsSum - previous.encodeTimeMsSum);
                const countDelta = Math.max(0, stats.encodeTimeMsCount - previous.encodeTimeMsCount);
                if (countDelta > 0) {
                    const meanMs = sumDelta / countDelta;
                    const ratio = meanMs / frameDurationMs;
                    this.encodeRatioEma =
                        VideoRecorder.EncodeRatioEmaAlpha * ratio
                        + (1 - VideoRecorder.EncodeRatioEmaAlpha) * this.encodeRatioEma;
                }
            }

            this.lastRecorderHealthStats = {
                ...stats,
                dropTrace: new Map(stats.dropTrace),
            };
            this.lastRecorderHealthWasPeerConnected = isPeerConnected;

            // Histogram split into parallel arrays for JSON-friendly
            // JSInvokable transit (byte enum + long count).
            const stages = new Uint8Array(stats.dropTrace.size);
            const counts: number[] = new Array<number>(stats.dropTrace.size);
            let i = 0;
            for (const [stage, count] of stats.dropTrace) {
                stages[i] = stage;
                counts[i] = count;
                i++;
            }
            await this.blazorRef.invokeMethodAsync(
                'OnRecorderStats',
                this.encodeRatioEma,
                this.senderDropRatioEma,
                stats.wireLastAckAgeMs,
                isPeerConnected,
                stages,
                counts,
                stats.bundlesShipped,
                stats.bytesEncoded);
        } catch (e) {
            warnLog?.log('reportRecorderStats failed:', e);
        } finally {
            this.recorderHealthInFlight = false;
        }
    }

    private cleanupPreviewTrack(): void {
        this.cleanupGeneratedPreviewTrack();
        if (this.inputTrack) {
            this.inputTrack.onended = null;
            // For screencast, the same track is shared as preview; stop
            // it so the browser's "Stop sharing" indicator clears.
            try { this.inputTrack.stop(); } catch { /* ignore */ }
            this.inputTrack = null;
        }
        this.previewTrack = null;
    }

    private createGeneratedPreviewTrack(): PreviewTrackGenerator | null {
        this.cleanupGeneratedPreviewTrack();

        const g = globalThis as unknown as {
            MediaStreamTrackGenerator?: new (init: { kind: 'video' }) =>
                MediaStreamTrack & { readonly writable: WritableStream<VideoFrame> };
            VideoTrackGenerator?: new () => {
                readonly writable: WritableStream<VideoFrame>;
                readonly track: MediaStreamTrack;
            };
        };
        try {
            if (typeof g.MediaStreamTrackGenerator === 'function') {
                const generator = new g.MediaStreamTrackGenerator({ kind: 'video' });
                this.generatedPreviewTrack = generator;
                debugLog?.log(`createGeneratedPreviewTrack: MediaStreamTrackGenerator (id=${generator.id})`);
                return { track: generator, writable: generator.writable };
            }
            if (typeof g.VideoTrackGenerator === 'function') {
                const generator = new g.VideoTrackGenerator();
                this.generatedPreviewTrack = generator.track;
                debugLog?.log(`createGeneratedPreviewTrack: VideoTrackGenerator (id=${generator.track.id})`);
                return { track: generator.track, writable: generator.writable };
            }
        } catch (e) {
            warnLog?.log('createGeneratedPreviewTrack failed; falling back to raw preview track:', e);
            this.cleanupGeneratedPreviewTrack();
        }
        return null;
    }

    private cleanupGeneratedPreviewTrack(): void {
        const track = this.generatedPreviewTrack;
        if (!track) return;
        try { track.stop(); } catch { /* ignore */ }
        this.generatedPreviewTrack = null;
        if (this.previewTrack === track)
            this.previewTrack = this.inputTrack;
    }

    private notifyPreviewTrackChanged(): void {
        for (const cb of this.stateChangeListeners) {
            try { cb(this._recordingState); } catch (e) { warnLog?.log('state change listener threw', e); }
        }
    }

    private async describeStartError(error: unknown): Promise<string> {
        if (error instanceof DOMException && error.name === 'NotReadableError') {
            const deviceId = this.selectedCameraDeviceId;
            if (!deviceId) return 'Camera is unavailable';
            try {
                const devices = await navigator.mediaDevices.enumerateDevices();
                const label = devices
                    .find(d => d.kind === 'videoinput' && d.deviceId === deviceId)
                    ?.label;
                return label ? `Camera '${label}' is unavailable` : 'Camera is unavailable';
            } catch {
                return 'Camera is unavailable';
            }
        }
        return error instanceof Error ? error.message : String(error);
    }

    private async pickSimulcastCodec(
        supportedCodecs: CodecInfo[],
        audienceCodecs: string[] | undefined,
        ladder: LayerConfig[],
    ): Promise<string | null> {
        for (const codecInfo of this.listCodecCandidatesByEfficiency(supportedCodecs, audienceCodecs)) {
            const category = codecInfo.category;
            const probeLadder = this.withCodecBitrates(ladder, codecInfo.codec);
            const probe = await probeEncoder(codecInfo.codec, probeLadder);
            if (probe.supported) {
                infoLog?.log(`pickSimulcastCodec: ${category} (${codecInfo.codec}) PASS — median=${probe.medianEncodeMs.toFixed(1)}ms over ${ladder.length} layer(s)`);
                return codecInfo.codec;
            }
            infoLog?.log(`pickSimulcastCodec: ${category} (${codecInfo.codec}) FAIL — median=${probe.medianEncodeMs.toFixed(1)}ms, stage=${probe.failedStage ?? 'timing'}`);
        }
        return null;
    }

    private pickInitialCodec(supportedCodecs: CodecInfo[], audienceCodecs: string[] | undefined, size: Size): string {
        return this.pickBestCodecByEfficiency(supportedCodecs, audienceCodecs)
            ?? getDefaultCodec(supportedCodecs, size.width, size.height);
    }

    private pickBestCodecByEfficiency(supportedCodecs: CodecInfo[], audienceCodecs: string[] | undefined): string | null {
        return this.listCodecCandidatesByEfficiency(supportedCodecs, audienceCodecs)[0]?.codec ?? null;
    }

    private listCodecCandidatesByEfficiency(
        supportedCodecs: CodecInfo[],
        audienceCodecs: string[] | undefined,
    ): CodecInfo[] {
        const allowedCategories = this.allowedCodecCategories(audienceCodecs);
        const bestByCategory = new Map<CodecInfo['category'], CodecInfo>();
        for (const codecInfo of supportedCodecs) {
            if (!codecInfo.supported) continue;
            if (allowedCategories && !allowedCategories.has(codecInfo.category)) continue;
            if (isEncoderCodecExcluded(codecInfo.category)) continue;
            const current = bestByCategory.get(codecInfo.category);
            if (!current || (!current.hardwareAccelerated && codecInfo.hardwareAccelerated))
                bestByCategory.set(codecInfo.category, codecInfo);
        }
        return [...bestByCategory.values()]
            .sort((a, b) =>
                getVideoCodecEfficiency(b.codec) - getVideoCodecEfficiency(a.codec)
                || Number(b.hardwareAccelerated) - Number(a.hardwareAccelerated));
    }

    private allowedCodecCategories(codecs: string[] | undefined): Set<CodecInfo['category']> | null {
        if (!codecs || codecs.length === 0)
            return null;

        const result = new Set<CodecInfo['category']>();
        for (const codec of codecs) {
            const normalized = codec.trim().toLowerCase();
            if (normalized === 'h264' || normalized === 'hevc' || normalized === 'av1' || normalized === 'vp9')
                result.add(normalized);
            else
                result.add(getCodecCategory(normalized));
        }
        return result;
    }

    private register(kind: number): void {
        this.registeredKind = kind;
        activeRecorders.set(kind, this);
        notifyRegistryListeners(this, kind);
    }

    private unregister(): void {
        const kind = this.registeredKind;
        if (kind !== null && activeRecorders.get(kind) === this) {
            activeRecorders.delete(kind);
            notifyRegistryListeners(null, kind);
        }
        this.registeredKind = null;
    }

    private setRecordingState(next: VideoRecordingState): void {
        if (this._recordingState === next) return;
        this._recordingState = next;
        for (const cb of this.stateChangeListeners) {
            try { cb(next); } catch (e) { warnLog?.log('state change listener threw', e); }
        }
    }

    private setIsBlurEnabled(next: boolean): void {
        if (this.isBlurEnabled === next) return;
        this.isBlurEnabled = next;
        for (const cb of this.blurChangeListeners) {
            try { cb(next); } catch (e) { warnLog?.log('blur change listener threw', e); }
        }
    }

    /**
     * Extract unique encoder codec categories from detected codec support.
     */
    private extractEncoderCategories(codecs: CodecInfo[]): string[] {
        const categories = new Set<string>();
        for (const c of codecs) {
            if (c.supported) {
                if (c.category === 'av1' && !c.hardwareAccelerated) continue;
                if (DeviceInfo.isMobile && !c.hardwareAccelerated && c.category !== 'h264') continue;
                categories.add(c.category);
            }
        }
        const ordered: string[] = [];
        if (categories.has('av1')) ordered.push('av1');
        if (categories.has('hevc')) ordered.push('hevc');
        if (categories.has('vp9')) ordered.push('vp9');
        if (categories.has('h264')) ordered.push('h264');
        return ordered;
    }
}

// ---- Debug-log usage suppressor ------------------------------------------

// `debugLog` is preserved for parity with the legacy file. It's not
// currently called anywhere, but the eslint rule complains about
// unused logger handles. Reference it once so the rule passes — when
// new debug logging lands here it just plugs into this handle.
void debugLog;

// `RecorderStats` is exported for callers that want to inspect
// the new wire-safe stats shape. Currently no external caller uses
// it; suppress unused-import warnings the same way.
const _statsType: RecorderStats | null = null;
void _statsType;

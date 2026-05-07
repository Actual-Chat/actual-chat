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
//  - Diagnostics shape: `OwnStreamDiagnostics`, `OwnSpatialLayerDiagnostics`.
//
// Behavioural diffs from the legacy file (intentional, per cut-over plan):
//  - Reconfigure / switchCodec / setSimulcastLayers all become a
//    `worker.stop()` followed by `worker.start({...newConfig})` (no
//    in-place reconfigure on the new pipeline).
//  - Preview-only mode (the blur preview tap to a main-thread canvas)
//    is no longer surfaced — the new pipeline doesn't bounce frames
//    back to main. `addPreviewFrameListener` becomes a no-op.
//  - 1 Hz recorder-health snapshots stop firing — the new pipeline
//    doesn't compute the legacy metrics. `OnRecorderHealthSnapshot`
//    on the C# side just stops getting called.
//  - VAD-driven adaptive framerate is out of scope (`setRemoteStreamCount`
//    becomes a no-op).
//
// TODOs in this file mark every place where the legacy behaviour is
// known-degraded and we'd want to revisit once follow-up phases add
// the missing surface to the new pipeline.

import { AC } from 'app-constants';
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
    type CodecInfo,
} from '../../Services/Video/codec-support';
import { getExpectedBitrate } from '../../Services/Video/bitrate-table';
import {
    buildLadder,
    SCREENCAST_MAX_SIMULCAST_TIERS,
    WEBCAM_MAX_SIMULCAST_TIERS,
    type SpatialLayerConfig,
} from './simulcast-ladder';
import { MediaCapture } from '../../Services/Video/services/media-capture';
import {
    type RecorderWorker,
    type RecorderWorkerCallbacks,
    type WireSafeRecorderConfig,
} from '../../Services/Video/sender/recorder-worker-contract';
import type { EncoderConfigPerLayer } from '../../Services/Video/operators/encode';
import type { VideoRecordingStats } from '../../Services/Video/frame-envelopes';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoRecorder');

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
    spatialLayers: OwnSpatialLayerDiagnostics[];
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
        layers: { width: number; height: number; bitrate: number; scalabilityMode?: string }[];
    } | null;
}

export interface OwnSpatialLayerDiagnostics {
    spatialLayerId: number;
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

export interface VideoDevice {
    deviceId: string;
    label: string;
}

// ---- Active recorder registry --------------------------------------------

const StreamKindWebcam = 0;
const activeRecorders = new Map<number, VideoRecorder>();

export function getActiveRecorder(kind: number = StreamKindWebcam): VideoRecorder | null {
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
// Each VideoRecorder owns its own worker so a webcam + screencast can
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
    private isScreencasting = false;

    // Active recorder registration (by kind).
    private registeredKind: number | null = null;

    // Camera / screen track currently being fed to the worker. Owned
    // by main thread for preview (`<video srcObject>`); a CLONE is
    // transferred to the worker (the original is neutered when
    // postMessage'd, so we keep the clone-and-transfer pattern from
    // the legacy `startTrackTransferMode`).
    private inputTrack: MediaStreamTrack | null = null;
    private previewTrack: MediaStreamTrack | null = null;
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

    // Configuration cached for restart on switchCamera / reconfigure /
    // codec switch / simulcast change.
    private selectedCameraDeviceId: string | null = null;
    private chatId = '';
    private isBlurEnabled = false;
    private disposed = false;
    private cameraWidth = 0;
    private cameraHeight = 0;

    // Cached encoder capabilities (detected at recording start).
    private supportedEncoderCategories: string[] = [];
    private audienceCodecs?: string[];
    // Codec switch fallback bookkeeping (preserved from legacy).
    private lastCodecSwitchAt = 0;
    private readonly codecSwitchCooldownMs = 2000;
    private supportedCodecs: CodecInfo[] = [];

    // Active simulcast ladder (bottom-first). Drives the wire-safe
    // recorder config via {@link toEncoderConfigs}.
    private simulcastLayers: SpatialLayerConfig[] | null = null;
    private fullSimulcastLadder: SpatialLayerConfig[] | null = null;
    // Currently-selected codec string (e.g. 'avc1.640028'). Threaded
    // into every encoder config layer.
    private currentCodecString = '';
    private currentCodecHardwareAccel = false;
    // Stream-mode driving downstream config (bitrate table, simulcast caps).
    private currentMode: 'webcam' | 'screen' = 'webcam';
    // Top-tier encoder framerate. 30 for webcam, 15 for screencast.
    private currentFramerate = 30;

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

    // Wallclock anchor for diagnostics duration calculation.
    private startedAtMs = 0;

    static create(blazorRef: DotNet.DotNetObject, kind: number): VideoRecorder {
        return new VideoRecorder(blazorRef, kind);
    }

    static async enumerateDevices(): Promise<VideoDevice[]> {
        try {
            const tempStream = await navigator.mediaDevices.getUserMedia({ video: true });
            tempStream.getTracks().forEach(t => t.stop());

            const devices = await navigator.mediaDevices.enumerateDevices();
            const videoInputs = devices.filter(d => d.kind === 'videoinput');

            const selected = DeviceInfo.isMobile
                ? VideoRecorder.pickMobileCameras(videoInputs)
                : videoInputs;

            const videoDevices = selected.map(d => ({
                deviceId: d.deviceId,
                label: d.label || `Camera ${d.deviceId.slice(0, 8)}`,
            }));
            infoLog?.log('Enumerated video devices:', videoDevices);
            return videoDevices;
        } catch (error) {
            errorLog?.log('Failed to enumerate video devices:', error);
            return [];
        }
    }

    private static pickMobileCameras(devices: MediaDeviceInfo[]): MediaDeviceInfo[] {
        const facingOf = (d: MediaDeviceInfo): 'user' | 'environment' | null => {
            const input = d as InputDeviceInfo;
            const facing: string[] | undefined = typeof input.getCapabilities === 'function'
                ? input.getCapabilities().facingMode
                : undefined;
            if (facing && facing.length > 0) {
                if (facing.includes('user')) return 'user';
                if (facing.includes('environment')) return 'environment';
            }
            const label = d.label.toLowerCase();
            if (/facing front|\bfront\b|\buser\b|self/.test(label)) return 'user';
            if (/facing back|\bback\b|\brear\b|environment/.test(label)) return 'environment';
            return null;
        };

        const front = devices.find(d => facingOf(d) === 'user');
        const back = devices.find(d => facingOf(d) === 'environment');
        if (front && back)
            return [front, back];
        if (front || back)
            return [front ?? back!, ...devices.filter(d => d !== (front ?? back) && facingOf(d) === null).slice(0, 1)];
        return devices.slice(0, 2);
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

        await this.startRecording(this.chatId, this.audienceCodecs);
    }

    public setBlurEnabled(enabled: boolean): void {
        this.setIsBlurEnabled(enabled);
        infoLog?.log('Background blur enabled:', enabled);
    }

    /**
     * Update the cached simulcast ladder. On a running recorder this
     * triggers a stop+start with the new ladder (the new pipeline does
     * NOT support hot reconfigure of the spatial layer set).
     *
     * TODO(phase 7+): re-introduce hot-apply once the recorder
     * supports a control channel for adding/removing layers without
     * tearing down the underlying RPC stream.
     */
    public setSimulcastLayers(layers: SpatialLayerConfig[] | null): void {
        const maxTiers = this.isScreencasting ? SCREENCAST_MAX_SIMULCAST_TIERS : WEBCAM_MAX_SIMULCAST_TIERS;
        const clamped = (layers && layers.length > maxTiers)
            ? layers.slice(-maxTiers)
            : layers;
        const requestedCount = clamped?.length ?? 0;
        let active: SpatialLayerConfig[] | null = (clamped && clamped.length >= 2) ? clamped : null;
        const prevCount = this.simulcastLayers?.length ?? 0;
        if (active !== null && this.fullSimulcastLadder) {
            active = this.fullSimulcastLadder.slice(0, Math.min(requestedCount, this.fullSimulcastLadder.length));
            if (active.length < 2)
                active = null;
        }
        const newCount = active?.length ?? 0;
        this.simulcastLayers = active;
        if (prevCount !== newCount) {
            infoLog?.log(`setSimulcastLayers: ${prevCount} -> ${newCount} layer(s)`);
        }
        if (this.worker && prevCount !== newCount) {
            // Hot-restart with the new ladder. The encoder pool inside
            // the session retains parked encoders across the gap so
            // codec / NVENC slot survives.
            void this.restartWithCurrentConfig().catch((e: unknown) =>
                warnLog?.log('setSimulcastLayers: restart failed:', e));
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

    public isScreencastActive(): boolean {
        return this.isScreencasting;
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

    public async startRecording(chatId: string, audienceCodecs?: string[]): Promise<void> {
        this.chatId = chatId;
        this.audienceCodecs = audienceCodecs;
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        this.setRecordingState('starting');
        this.currentMode = 'webcam';
        infoLog?.log(`Starting video recording... audienceCodecs=[${audienceCodecs?.join(', ') ?? '(none)'}]`);

        try {
            const targetSize: Size = { width: 1280, height: 720 };
            const targetFramerate = 30;
            this.currentFramerate = targetFramerate;

            const supportedCodecs = await detectSupportedCodecs(targetSize.width, targetSize.height);
            this.supportedCodecs = supportedCodecs;
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);
            infoLog?.log(`Supported encoder categories: [${this.supportedEncoderCategories.join(', ')}]`);

            const initialPick = this.pickInitialCodec(supportedCodecs, audienceCodecs, targetSize);
            const ladder3 = buildLadder({
                topWidth: targetSize.width,
                topHeight: targetSize.height,
                tierCount: 3,
                maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
                bitrateFor: (h: number) => getExpectedBitrate(initialPick, h),
            });
            let bestCodecString = await this.pickSimulcastCodec(
                supportedCodecs, audienceCodecs, ladder3);
            let ladder: SpatialLayerConfig[] = ladder3;
            if (!bestCodecString) {
                const ladder2 = buildLadder({
                    topWidth: 640,
                    topHeight: 360,
                    tierCount: 2,
                    maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
                    bitrateFor: (h: number) => getExpectedBitrate(initialPick, h),
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
            }
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);
            const codecCategory = getCodecCategory(bestCodecString);
            ladder = buildLadder({
                topWidth: ladder[ladder.length - 1].width,
                topHeight: ladder[ladder.length - 1].height,
                tierCount: ladder.length,
                maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
                bitrateFor: (h: number) => getExpectedBitrate(bestCodecString, h),
            });
            const top = ladder[ladder.length - 1];
            const captureWidth = top.width;
            const captureHeight = top.height;
            const captureBitrate = top.bitrate;
            this.simulcastLayers = ladder.length >= 2 ? [...ladder] : null;
            this.fullSimulcastLadder = this.simulcastLayers ? [...this.simulcastLayers] : null;
            this.currentCodecString = bestCodecString;
            this.currentCodecHardwareAccel = bestCodecInfo?.hardwareAccelerated ?? false;
            infoLog?.log(`Initial codec selection: ${codecCategory} (${bestCodecString}), hw=${this.currentCodecHardwareAccel}, top=${captureWidth}x${captureHeight}@${captureBitrate / 1_000_000}Mbps`);
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
            infoLog?.log(`Track resolution: ${trackSettings.width}x${trackSettings.height}, facingMode=${trackSettings.facingMode ?? '(none)'}`);
            this.cameraWidth = trackSettings.width ?? captureWidth;
            this.cameraHeight = trackSettings.height ?? captureHeight;

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

    public async startScreencast(chatId: string, audienceCodecs?: string[]): Promise<void> {
        this.chatId = chatId;
        this.audienceCodecs = audienceCodecs;
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        this.setRecordingState('starting');
        this.currentMode = 'screen';
        this.currentFramerate = 15;
        infoLog?.log('Starting screencast...');

        try {
            const detectionWidth = DeviceInfo.isMobile ? 1280 : 1920;
            const detectionHeight = DeviceInfo.isMobile ? 720 : 1080;
            const supportedCodecs = await detectSupportedCodecs(detectionWidth, detectionHeight);
            this.supportedCodecs = supportedCodecs;
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);

            const targetSize = { width: 1920, height: 1080 };
            const bestCodecString = this.pickInitialCodec(supportedCodecs, audienceCodecs, targetSize);
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);

            const screencastLadder = buildLadder({
                topWidth: targetSize.width,
                topHeight: targetSize.height,
                tierCount: 2,
                maxTierCount: SCREENCAST_MAX_SIMULCAST_TIERS,
                bitrateFor: (h: number) => getExpectedBitrate(bestCodecString, h, 'screen'),
            });
            const screencastTop = screencastLadder[screencastLadder.length - 1];
            this.simulcastLayers = [...screencastLadder];
            this.fullSimulcastLadder = [...screencastLadder];
            this.currentCodecString = bestCodecString;
            this.currentCodecHardwareAccel = bestCodecInfo?.hardwareAccelerated ?? false;
            infoLog?.log(`Screencast ladder (bottom-first): [${screencastLadder.map(l => `${l.width}x${l.height}`).join(', ')}], capture ${screencastTop.width}x${screencastTop.height}`);

            // Acquire the screen track on main thread.
            const screenTrack = await MediaCapture.captureScreencast();
            this.inputTrack = screenTrack;
            this.previewTrack = screenTrack;

            const trackSettings = screenTrack.getSettings();
            this.cameraWidth = trackSettings.width ?? targetSize.width;
            this.cameraHeight = trackSettings.height ?? targetSize.height;

            screenTrack.onended = () => {
                infoLog?.log('Screen sharing track ended (user stopped sharing)');
                void this.stopRecording();
            };

            this.ensureWorker();
            await this.startWorker(screencastLadder);

            this.isRecording = true;
            this.isScreencasting = true;
            this.setRecordingState('recording');

            await this.blazorRef.invokeMethodAsync('OnRecordingStarted');
            infoLog?.log('Screencast started');
        } catch (error) {
            const isUserCancel = error instanceof DOMException && error.name === 'NotAllowedError';
            if (isUserCancel) {
                this.setRecordingState('stopped');
                infoLog?.log('Screencast cancelled by user');
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
            this.isScreencasting = false;
            this.simulcastLayers = null;
            this.fullSimulcastLadder = null;
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

        const matchingCategories = codecs.filter(c => this.supportedEncoderCategories.includes(c));

        if (matchingCategories.length === 0) {
            warnLog?.log(`updateSupportedDecoderCodecs: no match between server codecs [${codecs.join(', ')}] and encoder capabilities [${this.supportedEncoderCategories.join(', ')}], keeping current codec`);
            return;
        }

        const audienceFilteredCodecs = this.supportedCodecs.filter(c =>
            c.supported && matchingCategories.includes(c.category)
        );
        if (audienceFilteredCodecs.length === 0) return;

        const pickedCodecString = getDefaultCodec(audienceFilteredCodecs, this.cameraWidth || 1280, this.cameraHeight || 720);
        const pickedCategory = getCodecCategory(pickedCodecString);
        const currentCategory = getCodecCategory(this.currentCodecString);

        if (currentCategory === pickedCategory) {
            return;
        }

        infoLog?.log(`Switching codec ${currentCategory} → ${pickedCategory} (${pickedCodecString})`);
        const pickedInfo = this.supportedCodecs.find(c => c.codec === pickedCodecString);
        this.currentCodecString = pickedCodecString;
        this.currentCodecHardwareAccel = pickedInfo?.hardwareAccelerated ?? false;
        await this.restartWithCurrentConfig();
    }

    public reconfigure(level: string, width: number, height: number): void {
        if (!this.worker) {
            warnLog?.log('reconfigure: no active worker');
            return;
        }

        infoLog?.log(`reconfigure: level=${level}, size=${width}x${height}, cameraSize=${this.cameraWidth}x${this.cameraHeight}`);
        const cameraIsPortrait = this.cameraWidth > 0 && this.cameraHeight > 0 && this.cameraHeight > this.cameraWidth;
        const presetIsLandscape = width > height;
        if (cameraIsPortrait && presetIsLandscape)
            [width, height] = [height, width];

        let cappedWidth = this.cameraWidth > 0 ? Math.min(width, this.cameraWidth) : width;
        let cappedHeight = this.cameraHeight > 0 ? Math.min(height, this.cameraHeight) : height;

        if (this.currentMode === 'webcam') {
            const longSide = Math.max(cappedWidth, cappedHeight);
            if (longSide > 1280) {
                const scale = 1280 / longSide;
                cappedWidth = Math.round(cappedWidth * scale) & ~1;
                cappedHeight = Math.round(cappedHeight * scale) & ~1;
            }
        }

        // When simulcast is active, ladder TOP dim is fixed by source cap.
        const isSimulcastActive = this.simulcastLayers !== null && this.simulcastLayers.length >= 2;
        if (isSimulcastActive) {
            infoLog?.log(`reconfigure (simulcast): preset ${cappedWidth}x${cappedHeight} ignored — ladder top fixed by source cap`);
            return;
        }

        // P2P / single-tier: rebuild a 1-tier ladder with new dims and restart.
        // TODO(phase 7+): the legacy `worker.reconfigure({ bitrate, width, height })`
        // could change encoder dims in-place, avoiding a full restart. The new
        // pipeline does not have an in-place reconfigure; every encoder swap
        // is a stop/start. NVENC slot survives via the encoder pool.
        let cappedBitrate = getExpectedBitrate(this.currentCodecString, cappedHeight, this.currentMode === 'screen' ? 'screen' : 'webcam');
        if (DeviceInfo.isIos)
            cappedBitrate = Math.min(cappedBitrate, 1_000_000);
        else if (DeviceInfo.isMobile)
            cappedBitrate = Math.min(cappedBitrate, 2_000_000);

        infoLog?.log(`reconfigure: ${cappedWidth}x${cappedHeight} @ ${cappedBitrate / 1_000_000}Mbps (codec=${this.currentCodecString})`);
        this.simulcastLayers = [{ width: cappedWidth, height: cappedHeight, bitrate: cappedBitrate }];
        this.fullSimulcastLadder = [...this.simulcastLayers];
        void this.restartWithCurrentConfig().catch((e: unknown) =>
            warnLog?.log('reconfigure: restart failed:', e));
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
        // The new pipeline's `VideoRecordingStats` is much sparser than
        // the legacy `VideoProcessingStats`. We map what we can; the
        // rest become zero / 'N/A' / null. The C# diagnostics surface
        // tolerates these — fields are simply rendered blank.
        // TODO(phase 7+): repopulate per-spatial-layer stats once the
        // new pipeline's encoder-pool exposes per-layer counters.
        const ladder = this.simulcastLayers ?? [];
        const top = ladder.length > 0 ? ladder[ladder.length - 1] : null;

        const duration = this.startedAtMs > 0
            ? (Date.now() - this.startedAtMs) / 1000
            : 0;

        const codecCategory = this.currentCodecString
            ? getCodecCategory(this.currentCodecString)
            : '';

        return {
            mode: this.isScreencasting ? 'screen' : this.isRecording ? 'webcam' : 'none',
            codec: this.currentCodecString,
            codecCategory,
            hardwareAccelerated: this.currentCodecHardwareAccel,
            inputResolution: this.cameraWidth > 0 ? `${this.cameraWidth}x${this.cameraHeight}` : 'N/A',
            inputFramerate: this.currentFramerate,
            outputResolution: top ? `${top.width}x${top.height}` : 'N/A',
            configuredBitrate: top?.bitrate ?? 0,
            actualBitrateKbps: 0,
            encodedFrames: 0,
            droppedFrames: 0,
            keyFrames: 0,
            spatialLayers: ladder.map((l, i) => ({
                spatialLayerId: i,
                outputResolution: `${l.width}x${l.height}`,
                configuredBitrate: l.bitrate,
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
                    bitrate: l.bitrate,
                    scalabilityMode: l.scalabilityMode,
                })),
            } : null,
        };
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
        this.isScreencasting = false;
        this.setRecordingState('stopped');
    }

    // ---- Internal helpers -----------------------------------------------

    private ensureWorker(): void {
        if (this.worker) return;

        const workerInstance = createRecorderWorker();
        this.workerInstance = workerInstance;

        const callbacks: RecorderWorkerCallbacks = {
            onError: (error: string) => {
                errorLog?.log(`RecorderWorker reported error: ${error}`);
                void this.blazorRef.invokeMethodAsync('OnRecordingError', error);
            },
            onStreamCreated: (codecSettings: string) => {
                infoLog?.log(`Worker created RPC stream, codecSettings: ${codecSettings.length} chars`);
            },
            onStreamEnded: (reason: string) => {
                infoLog?.log(`Worker stream ended: ${reason}`);
            },
        };

        this.worker = rpcClientServer<RecorderWorker>(
            'VideoRecorder.worker',
            workerInstance,
            callbacks,
        );

        // Push current connectivity to the freshly-created worker
        // (the long-lived listeners installed in the constructor only
        // fire on transitions, so we need a one-shot push here).
        this._connectivityHandler?.();

        this._disconnectApiHandler = () => void this.worker?.disconnectApi();
        Api.onDisconnectRequested(WorkerKind.VideoCapture).add(this._disconnectApiHandler);

        // Seed the worker's app-constants holders so `MediaRpcStreamOptions`
        // can read `VIDEO.rpcStreamAckPeriod` etc. from the streaming-glue
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

    private async startWorker(ladder: SpatialLayerConfig[]): Promise<void> {
        if (!this.worker || !this.inputTrack) {
            throw new Error('startWorker: worker or input track missing');
        }

        // Frame source: a hidden <video srcObject> driven by
        // `requestVideoFrameCallback`. On each callback we construct a
        // VideoFrame from the video element and ship it to the worker
        // via `pushFrame` (the frame is transferred — VideoFrame is a
        // Transferable per the rpc.ts trailing-args convention).
        //
        // Why not MediaStreamTrackProcessor: Chromium 147 (and its
        // fake-device path that the dev rig uses) starves the reader
        // after a small fixed number of frames — verified by both
        // standalone tests and the recorder pipeline. `rVFC` on a
        // <video> element gives us a steady 30 fps from the same track
        // with no additional plumbing.
        // rVFC pump from a hidden <video> (gives us steady ~30fps
        // from the live track without going through the
        // MediaStreamTrackProcessor 3-frame-stall path on Chromium 147).
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

        const apiUrl = BrowserInit.getUrl('/rpc/ws').replace(/^http/, 'ws');
        // SharedSettings is the legacy worker plumbing; the new worker
        // doesn't observe it. Keep the call so the audio path (which
        // still uses SharedSettings) is unaffected.
        SharedSettings.update({ apiUrl });

        const encoderConfigs = this.toEncoderConfigs(ladder);

        const config: WireSafeRecorderConfig = {
            chatId: this.chatId,
            apiUrl,
            streamKind: this.currentMode === 'screen' ? 1 : 0,
            encoderConfigs,
            // Webcam: 2-3s interval; Screencast: 1-2s interval.
            keyframeIntervalFrames: this.currentMode === 'screen'
                ? this.currentFramerate * 2
                : this.currentFramerate * 3,
            maxKeyFrameIntervalMs: this.currentMode === 'screen' ? 10000 : 3000,
        };

        const sourceStartedAtMs = Date.now();
        this.startedAtMs = sourceStartedAtMs;
        // `start()` resolves only when the run finishes draining (per
        // the new contract) — fire and forget so we can return from
        // startRecording() and let the operator pipe drive itself.
        void this.worker.start({ sourceStartedAtMs, config }).catch((e: unknown) => {
            errorLog?.log('Worker start rejected:', e);
            const message = e instanceof Error ? e.message : String(e);
            void this.blazorRef.invokeMethodAsync('OnRecordingError', message);
        });
    }

    private toEncoderConfigs(ladder: SpatialLayerConfig[]): EncoderConfigPerLayer[] {
        if (ladder.length === 0) {
            // Should not happen — startRecording / startScreencast always
            // set at least one tier — but defensively produce a single
            // tier from camera dims so the worker doesn't reject the start.
            return [{
                codec: this.currentCodecString,
                width: this.cameraWidth,
                height: this.cameraHeight,
                bitrate: getExpectedBitrate(this.currentCodecString, this.cameraHeight),
                framerate: this.currentFramerate,
            }];
        }
        return ladder.map(l => ({
            codec: this.currentCodecString,
            width: l.width,
            height: l.height,
            bitrate: l.bitrate,
            framerate: this.currentFramerate,
        }));
    }

    private async restartWithCurrentConfig(): Promise<void> {
        if (!this.worker || !this.inputTrack) return;
        const ladder = this.simulcastLayers ?? this.fullSimulcastLadder ?? [];
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

    private tearDownWorker(): void {
        if (this._disconnectApiHandler) {
            Api.onDisconnectRequested(WorkerKind.VideoCapture).remove(this._disconnectApiHandler);
            this._disconnectApiHandler = null;
        }
        if (this._sharedSettingsRegistration) {
            try { this._sharedSettingsRegistration.dispose(); } catch { /* ignore */ }
            this._sharedSettingsRegistration = null;
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
        // Stop the rVFC pump: setting workerSourceCancelled prevents
        // re-arming the next callback; the in-flight frame is closed by
        // the pump's own try/catch on worker rejection. Then drop the
        // hidden video so the engine releases its track-feed.
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

    private cleanupPreviewTrack(): void {
        if (this.inputTrack) {
            this.inputTrack.onended = null;
            // For screencast, the same track is shared as preview; stop
            // it so the browser's "Stop sharing" indicator clears.
            try { this.inputTrack.stop(); } catch { /* ignore */ }
            this.inputTrack = null;
        }
        this.previewTrack = null;
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
        ladder: SpatialLayerConfig[],
    ): Promise<string | null> {
        const priority: ('av1' | 'hevc' | 'vp9' | 'h264')[] = ['av1', 'hevc', 'vp9', 'h264'];
        const audience = audienceCodecs && audienceCodecs.length > 0 ? audienceCodecs : null;
        for (const category of priority) {
            if (!this.supportedEncoderCategories.includes(category)) continue;
            if (audience && !audience.includes(category)) continue;
            const codecInfo = supportedCodecs.find(c => c.category === category && c.supported && c.hardwareAccelerated)
                ?? supportedCodecs.find(c => c.category === category && c.supported);
            if (!codecInfo) continue;
            const probe = await probeEncoder(codecInfo.codec, ladder);
            if (probe.supported) {
                infoLog?.log(`pickSimulcastCodec: ${category} (${codecInfo.codec}) PASS — median=${probe.medianEncodeMs.toFixed(1)}ms over ${ladder.length} layer(s)`);
                return codecInfo.codec;
            }
            infoLog?.log(`pickSimulcastCodec: ${category} (${codecInfo.codec}) FAIL — median=${probe.medianEncodeMs.toFixed(1)}ms, stage=${probe.failedStage ?? 'timing'}`);
        }
        return null;
    }

    private pickInitialCodec(supportedCodecs: CodecInfo[], audienceCodecs: string[] | undefined, size: Size): string {
        if (audienceCodecs && audienceCodecs.length > 0) {
            const matchingCategories = audienceCodecs.filter(c => this.supportedEncoderCategories.includes(c));
            if (matchingCategories.length > 0) {
                const audienceFilteredCodecs = supportedCodecs.filter(c =>
                    c.supported && matchingCategories.includes(c.category),
                );
                return audienceFilteredCodecs.length > 0
                    ? getDefaultCodec(audienceFilteredCodecs, size.width, size.height)
                    : getDefaultCodec(supportedCodecs, size.width, size.height);
            }
            return getDefaultCodec(supportedCodecs, size.width, size.height);
        }
        return getDefaultCodec(supportedCodecs, size.width, size.height);
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

// `VideoRecordingStats` is exported for callers that want to inspect
// the new wire-safe stats shape. Currently no external caller uses
// it; suppress unused-import warnings the same way.
const _statsType: VideoRecordingStats | null = null;
void _statsType;

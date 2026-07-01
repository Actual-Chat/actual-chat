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
//  - Preview frames are surfaced from the normalized pipeline: preferably via
//    a generated track, with a main-thread canvas fallback matching playback.
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
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import { Versioning } from 'versioning';
import { DeviceInfo } from 'device-info';
import { ScreenOrientation } from 'orientation';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import { ConnectivityUI } from '../../../UI.Blazor/Services/ConnectivityUI/connectivity-ui';
import { SharedSettings } from 'shared-settings';
import { SharedSettingsWorkerSync } from 'shared-settings-worker';
import {
    detectSupportedCodecs,
    getDefaultCodec,
    getSoftwareH264Codec,
    getCodecCategory,
    getActiveEncoderCategoriesByPriority,
    probeEncoder,
    excludeEncoderCodec,
    isEncoderCodecExcluded,
    isEncoderCodecProven,
    markEncoderCodecProven,
    type CodecInfo,
} from '../../Services/Video/codec-support';
import {
    buildLadder,
    fpsForRequestedLayer,
    type LayerConfig,
} from './layer-ladder';
import { MediaCapture } from '../../Services/Video/services/media-capture';
import {
    type PreviewFramePresentation,
    type RecorderWorker,
    type RecorderWorkerCallbacks,
    type WireSafeRecorderConfig,
} from '../../Services/Video/sender/recorder-worker-contract';
import { consumeVideoTraceKill, registerVideoTraceKillWorker } from '../../Services/Video/video-trace-kill-control';
import { getDownscalerMode } from '../../Services/Video/downscaler-mode';
import {
    isEncoderInitFailedError,
    parseEncoderInitFailedCodec,
    type EncoderConfigPerLayer,
} from '../../Services/Video/operators/encode';
import type { RecorderStats } from '../../Services/Video/frame-envelopes';
import { pickRenderBackendKind } from './render-backend-selection';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoRecorder');
const RECORDER_HEALTH_INTERVAL_MS = 1000;
// Hold full fps this long after voice activity stops, so demand-driven pacing
// doesn't flap the encoder rate through the natural gaps between words.
const VAD_FPS_HOLD_MS = 2000;
// Cap on consecutive failed recovery attempts before we surface a fatal error
// to the user. With the existing backoff (200ms × 1.7^n, capped at 3s), 5
// attempts span ~9s — long enough to absorb a transient blip, short enough
// that the user is not left staring at a stalled call.
const MAX_RECOVERY_ATTEMPTS = 5;
// Foreground capture-stall window before we force a recovery restart. Chrome
// reclaims an idle WebCodecs encoder once the tab has been backgrounded
// ("Codec reclaimed due to inactivity"); the frame-driven recovery path only
// fires once a frame reaches the dead encoder, so a source that doesn't resume
// on foreground leaves the pipeline silently dead. 3s comfortably clears the
// ~1s health tick and brief capture gaps without churning a healthy stream.
const CAPTURE_STALL_RECOVERY_MS = 3000;
// User-facing message shown when the HW encoder cannot be initialised at all
// (every codec probe fails) or when recovery has exhausted MAX_RECOVERY_ATTEMPTS.
// Kept free of codec/encoder/internals — actionable only.
const USER_FACING_RESTART_MESSAGE =
    'Please restart the app or device to be able to use video.';

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
        layers: { width: number; height: number; bitrateKbps: number }[];
    } | null;
    // Cumulative drop-stage histogram from the active RecorderStats sample.
    // Keys are decimal FrameDropStage values; only non-zero stages are
    // emitted.
    dropTraceByStage: Record<string, number>;
    // Demand inputs driving layer pruning + fps pacing.
    activeLayerCount: number;
    receiverLayerCap: number;
    healthLayerCap: number;
    lastMaxLayerId: number;
    targetFps: number;
    isSpeaking: boolean;
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
}

// Preview frame listener used by the canvas fallback when generated preview
// tracks are unavailable. Async listeners must return their Promise; the frame
// is closed after all returned listener work settles.
export type PreviewFrameListener = (frame: VideoFrame) => void | Promise<void>;
export type PreviewPresentationListener = (presentation: PreviewFramePresentation | null) => void;

export type VideoRecordingState = 'stopped' | 'warming-up' | 'starting' | 'recording' | 'error';

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

function isPromiseLike(value: unknown): value is PromiseLike<void> {
    return Boolean(value
        && typeof value === 'object'
        && 'then' in value
        && typeof value.then === 'function');
}

// Bottom-first camera simulcast tier sizes (mirrors VideoLayerDef.CameraLayers
// in C#). The 1920×1080 top tier is desktop-only and non-H264; it's added by
// the QC ramp (receiver demand / encode+bandwidth headroom) on top of the
// ½-derived 184/360/720 base (bottom rounded to mod-8 for HEVC; see
// DERIVED_TIER_MULTIPLE in layer-ladder.ts). 720→1080 is ×1.5, so an explicit
// size list is used instead of the ½-derivation.
const CAMERA_TIER_SIZES = [
    { width: 320, height: 184 },
    { width: 640, height: 360 },
    { width: 1280, height: 720 },
    { width: 1920, height: 1080 },
] as const;

// Standalone top-tier encoder probe, callable before any VideoRecorder
// instance exists. Mirrors the dims and tier count that `startRecording`
// would pick (3-tier 1280×720 on desktop, 2-tier 640×360 on mobile) so the
// `probeEncoder` cache hits when the user actually clicks Start Video and
// the recorder's own `pickSimulcastCodec` runs.
//
// Returns the codec category that passed, or null if every active
// HW-accelerated candidate failed. Used by JoinVideoCallModal to detect a
// machine-level encoder wedge while the user is still in the preview UI.
export async function probeTopTierEncoderSupport(): Promise<CodecInfo['category'] | null> {
    const isMobile = DeviceInfo.isMobile;
    const targetSize = isMobile
        ? { width: 640, height: 360 }
        : { width: 1280, height: 720 };
    const tierCount = isMobile ? 2 : 3;
    const supportedCodecs = await detectSupportedCodecs(targetSize.width, targetSize.height);
    // Priority comes from codec-support.ts REPRESENTATIVE_CODECS via the
    // exported helper — the same list that drives detection, default-codec
    // selection, and audience ordering. Re-enabling a category there (e.g.
    // AV1) propagates here automatically; no hardcoded list to keep in sync.
    const candidates = getActiveEncoderCategoriesByPriority()
        .map(cat => supportedCodecs.find(c => c.category === cat && c.supported && c.hardwareAccelerated))
        .filter((c): c is CodecInfo => Boolean(c));
    if (candidates.length === 0) {
        warnLog?.log('probeTopTierEncoderSupport: no HW-accelerated candidates available');
        return null;
    }

    async function probeOnce(layers: { width: number; height: number; bitrateKbps?: number; baseBitrateKbps?: number }[],
        hwAccel: HardwareAcceleration,
        label: string): Promise<CodecInfo['category'] | null> {
        for (const codec of candidates) {
            const layersWithBitrates = layers.map(l => {
                const baseBitrateKbps = l.baseBitrateKbps ?? l.bitrateKbps ?? 0;
                return {
                    ...l,
                    baseBitrateKbps,
                    bitrateKbps: getVideoLayerBitrateKbps(baseBitrateKbps, codec.codec),
                };
            });
            const result = await probeEncoder(codec.codec, layersWithBitrates, undefined, undefined, hwAccel);
            if (result.supported) {
                infoLog?.log(
                    `probeTopTierEncoderSupport: ${codec.category} (${codec.codec}) PASS ` +
                    `@ ${targetSize.width}x${targetSize.height} (${label})`);
                return codec.category;
            }
            warnLog?.log(
                `probeTopTierEncoderSupport: ${codec.category} (${codec.codec}) ` +
                `FAIL stage=${result.failedStage} (${label})`);
        }
        return null;
    }

    // Attempt 1: standard tier ladder with prefer-hardware. Mirrors the
    // recorder's first startRecording attempt.
    const ladderHW = buildLadder({
        topWidth: targetSize.width,
        topHeight: targetSize.height,
        tierCount,
        maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
        bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
    });
    const hwPass = await probeOnce(ladderHW, 'prefer-hardware', `${tierCount} layer(s), prefer-hardware`);
    if (hwPass) return hwPass;

    // Attempt 2: 1-tier no-preference SW fallback. Mirrors the recorder's
    // last-resort fallback. On AMD iGPU + Windows MFT (or any device where
    // HW encoder activation is broken / exhausted), the SW OpenH264 path
    // is independent of VCN sessions and will pass here. Without this the
    // modal would gate the user behind "restart" while the recorder's
    // SW fallback would actually have worked.
    const ladderSW = buildLadder({
        topWidth: targetSize.width,
        topHeight: targetSize.height,
        tierCount: 1,
        maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
        bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
    });
    const swPass = await probeOnce(ladderSW, 'no-preference', '1 layer, no-preference (SW fallback)');
    if (swPass) return swPass;

    return null;
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
    private previewCanvasFallback = false;
    private generatedPreviewTrack: MediaStreamTrack | null = null;
    private previewFramePresentation: PreviewFramePresentation | null = null;
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
    // Active layer count = min(healthLayerCap, receiverLayerCap, fullLayerLadder).
    // healthLayerCap: outbound QC target (encode + bandwidth health, soft-started
    // below the ceiling so the top tier is only added once lower tiers are smooth).
    // receiverLayerCap: max tier any viewer requests. The min() ensures receiver
    // demand can't pull in the top tier before the sender's health allows it.
    private healthLayerCap = Number.MAX_SAFE_INTEGER;
    private receiverLayerCap = Number.MAX_SAFE_INTEGER;
    // Demand-driven fps inputs: last aggregate requested layer, and whether the
    // local user is speaking (VAD). Speaking forces full rate so the active
    // speaker never goes choppy; a short hold rides through natural pauses.
    private lastMaxLayerId = -1;
    private isSpeaking = false;
    private speakingHoldTimer: ReturnType<typeof setTimeout> | null = null;
    // Last fps target pushed to the worker (-1 = none pushed yet / no pacing).
    private lastTargetFps = -1;
    // Codec switch fallback bookkeeping (preserved from legacy).
    private lastCodecSwitchAt = 0;
    private readonly codecSwitchCooldownMs = 2000;
    private supportedCodecs: CodecInfo[] = [];

    // Active simulcast ladder (bottom-first). Drives the wire-safe
    // recorder config via {@link toEncoderConfigs}.
    private layers: LayerConfig[] | null = null;
    private fullLayerLadder: LayerConfig[] | null = null;
    // Warmup-time top-tier dims (post-orientation-flip). openGate uses
    // them to expand the ladder while preserving the warmup encoder's
    // resolution + aspect.
    private warmupTopSize: Size | null = null;
    // Currently-selected codec string (e.g. 'avc1.640028'). Threaded
    // into every encoder config layer.
    private currentCodecString = '';
    private currentCodecHardwareAccel = false;
    // HW-acceleration mode for the runtime encoder, chosen by the
    // startRecording fallback chain. Default 'prefer-hardware'; flipped to
    // 'no-preference' when the 1-tier last-resort fallback engages so the
    // runtime encoder matches the config that actually probed working.
    private currentHardwareAcceleration: HardwareAcceleration = 'prefer-hardware';
    // Set once the desktop-only SW-H.264 fallback engages (after HW recovery is
    // exhausted). Locks the recorder to prefer-software H.264 and blocks server-
    // or health-driven switches back into a wedged HW codec for the session.
    private softwareFallbackEngaged = false;
    // Stream-mode driving downstream config (simulcast caps and layer bitrates).
    private currentMode: 'camera' | 'screen' = 'camera';
    // Top-tier encoder framerate; undefined until a recording starts. Set from
    // `VIDEO.frameRate` (camera) or `track.getSettings().frameRate` (screencast).
    private currentFramerate: number | undefined;

    // Listeners.
    private previewFrameListeners = new Set<PreviewFrameListener>();
    private previewPresentationListeners = new Set<PreviewPresentationListener>();
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
    // Wallclock when foreground capture first flatlined; 0 when capture is live.
    private captureStallSinceMs = 0;

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
     * Update the cached simulcast ladder. When the running worker can
     * absorb the change (same codec, same source dims), we route through
     * {@link RecorderWorker.reconfigureLayers} so the wire RpcStream stays
     * open — receivers see the new layer count on per-frame `LayerCount`
     * without an end-of-stream blink. Codec or source-dim changes still
     * fall back to a full stop+start.
     */
    public setLayers(layers: LayerInput[] | null): void {
        // C# outbound QC health target. Record it as the health cap; the active
        // count is min(health, receiver). null/short means the QC wants a single
        // (non-simulcast) encoder.
        const maxTiers = this.isScreenCasting
            ? VIDEO.screenCastLayerBaseBitratesKbps.length
            : VIDEO.cameraLayerBaseBitratesKbps.length;
        const clamped = (layers && layers.length > maxTiers)
            ? layers.slice(-maxTiers)
            : layers;
        const normalized = clamped?.map(l => this.normalizeLayerInput(l)) ?? null;
        this.healthLayerCap = (normalized && normalized.length >= 2) ? normalized.length : 1;
        this.applyEffectiveLayers();
    }

    // Apply min(healthLayerCap, receiverLayerCap, fullLayerLadder.length) tiers by
    // slicing the full ladder. Single-encoder mode (count < 2) sets layers = null.
    private applyEffectiveLayers(): void {
        const full = this.fullLayerLadder;
        const requestedCount = full
            ? Math.min(full.length, this.healthLayerCap, this.receiverLayerCap)
            : 0;
        const active: LayerConfig[] | null = (full && requestedCount >= 2)
            ? this.withCodecBitrates(full.slice(0, requestedCount), this.currentCodecString)
            : null;
        const prevCount = this.layers?.length ?? 0;
        const newCount = active?.length ?? 0;
        this.layers = active;
        if (prevCount !== newCount)
            infoLog?.log(
                `applyEffectiveLayers: health=${this.healthLayerCap} receiver=${this.receiverLayerCap} ` +
                `→ ${prevCount} -> ${newCount} layer(s)`);
        if (this.worker && prevCount !== newCount) {
            // Codec / source dims unchanged → hot-apply without tearing down the
            // wire stream. The worker mutates the running pipeline's
            // LayerLadderController; spatialize, encode and wireSend pick up the
            // change on the next bundle.
            const ladderForWorker = this.resolveActiveLadder();
            const encoderConfigs = this.toEncoderConfigs(ladderForWorker);
            if (encoderConfigs.length > 0) {
                void this.worker.reconfigureLayers(encoderConfigs).catch((e: unknown) => {
                    warnLog?.log('applyEffectiveLayers: reconfigureLayers failed, falling back to restart:', e);
                    void this.restartWithCurrentConfig().catch((restartErr: unknown) =>
                        warnLog?.log('applyEffectiveLayers: restart fallback failed:', restartErr));
                });
            } else {
                warnLog?.log('applyEffectiveLayers: empty ladder — falling back to restart');
                void this.restartWithCurrentConfig().catch((e: unknown) =>
                    warnLog?.log('applyEffectiveLayers: restart failed:', e));
            }
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
        return this.previewTrack;
    }

    public getPreviewUsesCanvas(): boolean {
        return this.previewCanvasFallback;
    }

    public getPreviewFramePresentation(): PreviewFramePresentation | null {
        return this.previewFramePresentation;
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

    public addPreviewPresentationListener(cb: PreviewPresentationListener): () => void {
        this.previewPresentationListeners.add(cb);
        return () => this.previewPresentationListeners.delete(cb);
    }

    public addStateChangeListener(cb: (state: VideoRecordingState) => void): () => void {
        this.stateChangeListeners.add(cb);
        return () => this.stateChangeListeners.delete(cb);
    }

    public addBlurChangeListener(cb: (enabled: boolean) => void): () => void {
        this.blurChangeListeners.add(cb);
        return () => this.blurChangeListeners.delete(cb);
    }

    // Run the real recorder pipeline with the wire-gate CLOSED so encoded
    // chunks are discarded before reaching the server. Used by
    // JoinVideoCallModal to prove the encoder works on actual camera frames
    // before the user clicks Join. `openGate` flips the same pipeline into
    // live mode without restarting the encoder. Single top-tier layer is
    // enough to verify the hardest case; lower tiers spin up on openGate.
    //
    // Picks the codec via static support metadata only — `isConfigSupported`
    // is run by `detectSupportedCodecs`; no synthetic OffscreenCanvas probe.
    // The encoder either works on real frames or doesn't, and a failure
    // surfaces via the same OnRecordingError path as `startRecording`.
    public async warmup(chatId: string, audienceCodecs?: string[]): Promise<void> {
        if (this.isRecording) {
            warnLog?.log('warmup: already recording or warming up');
            return;
        }
        this.chatId = chatId;
        this.audienceCodecs = audienceCodecs;
        this.currentMaxLayerCount = 1;
        this.setRecordingState('warming-up');
        this.currentMode = 'camera';
        infoLog?.log(`Warmup starting... audienceCodecs=[${audienceCodecs?.join(', ') ?? '(none)'}]`);

        try {
            const isMobile = DeviceInfo.isMobile;
            // Same gate startRecording uses: explicit portrait request only
            // on Android (iOS Safari's MSTP doesn't auto-rotate; rotation is
            // baked into wire metadata instead of pixels).
            const wantsPortrait = isMobile
                && !DeviceInfo.isIos
                && ScreenOrientation.isObserved
                && ScreenOrientation.isPortrait;
            const targetFramerate = VIDEO.frameRate;
            this.currentFramerate = targetFramerate;

            // Capability probe at a safe baseline; the actual capture top is
            // resolved after the codec is known.
            const probeBase: Size = isMobile
                ? { width: 640, height: 360 }
                : { width: 1280, height: 720 };
            const probeSize: Size = wantsPortrait
                ? { width: probeBase.height, height: probeBase.width }
                : probeBase;

            const supportedCodecs = await detectSupportedCodecs(probeSize.width, probeSize.height);
            this.supportedCodecs = supportedCodecs;
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);

            const codecString = this.pickInitialCodec(supportedCodecs, audienceCodecs, probeSize);
            const codecInfo = supportedCodecs.find(c => c.codec === codecString);
            this.currentCodecString = codecString;
            this.currentCodecHardwareAccel = codecInfo?.hardwareAccelerated ?? false;
            this.currentHardwareAcceleration = 'prefer-hardware';

            // Desktop non-H264 captures at 1080 so the QC ramp can later hot-add
            // a real 1080 top tier (downscaled for lower tiers) — but only when a
            // 1080 layer actually exists. H264 caps at 720; mobile at 360.
            const supportsTopTier = getCodecCategory(codecString) !== 'h264'
                && VIDEO.cameraLayerBaseBitratesKbps.length >= 4;
            const captureBase: Size = isMobile
                ? { width: 640, height: 360 }
                : (supportsTopTier
                    ? { width: 1920, height: 1080 }
                    : { width: 1280, height: 720 });
            const requestSize: Size = wantsPortrait
                ? { width: captureBase.height, height: captureBase.width }
                : captureBase;
            infoLog?.log(
                `Warmup orientation pre-capture: isMobile=${isMobile}, ` +
                `isIos=${DeviceInfo.isIos}, ` +
                `screen.isObserved=${ScreenOrientation.isObserved}, ` +
                `screen.isPortrait=${ScreenOrientation.isPortrait}, ` +
                `wantsPortrait=${wantsPortrait}, codec=${codecString}, ` +
                `requestTarget=${requestSize.width}x${requestSize.height}`);

            // Build the FULL bottom-first simulcast ladder up front. Warmup runs
            // the normal pipeline gate-closed and encodes only L0 (the lowest
            // tier); the QC ramp grows upward once live. `normalize`/preview stay
            // at the ceiling (full-ladder top) regardless — see startWorker's
            // normalizeSize. (No special single-TOP-tier warmup encoder, so no
            // index remap when openGate expands.)
            const warmupTierCeiling = isMobile ? 2 : (supportsTopTier ? 4 : 3);
            let ladder = this.withCodecBitrates(buildLadder({
                topWidth: requestSize.width,
                topHeight: requestSize.height,
                tierCount: warmupTierCeiling,
                maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
                bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
                ...(supportsTopTier ? { tierSizes: CAMERA_TIER_SIZES } : {}),
            }), codecString);
            infoLog?.log(`Warmup codec=${codecString} (hw=${this.currentCodecHardwareAccel}) at ${requestSize.width}x${requestSize.height}, full ladder ${warmupTierCeiling} tier(s)`);

            const track = await MediaCapture.captureCameraStream({
                deviceId: this.selectedCameraDeviceId ?? undefined,
                frameRate: targetFramerate,
                width: requestSize.width,
                height: requestSize.height,
            });
            this.inputTrack = track;
            this.previewTrack = track;

            const trackSettings = track.getSettings();
            infoLog?.log(
                `Warmup track resolution: ${trackSettings.width}x${trackSettings.height}, ` +
                `frameRate=${trackSettings.frameRate ?? '(none)'}, ` +
                `facingMode=${trackSettings.facingMode ?? '(none)'}`);
            this.cameraWidth = trackSettings.width ?? requestSize.width;
            this.cameraHeight = trackSettings.height ?? requestSize.height;
            this.currentFramerate = trackSettings.frameRate ?? targetFramerate;

            // Same orientation reconciliation as startRecording: if the
            // browser returned a portrait sensor but our ladder is landscape,
            // flip the ladder so the encoder targets portrait — otherwise
            // normalizeFrame center-crops a landscape band out of a portrait
            // frame and the receiver sees only the middle (e.g. just the face).
            const cameraIsPortrait = this.cameraHeight > this.cameraWidth;
            const ladderTopIsPortrait = ladder[ladder.length - 1].height > ladder[ladder.length - 1].width;
            if (!wantsPortrait && cameraIsPortrait !== ladderTopIsPortrait) {
                ladder = ladder.map(l => ({ ...l, width: l.height, height: l.width }));
                infoLog?.log(
                    `Warmup: camera orientation mismatch — flipped ladder to: ` +
                    `[${ladder.map(l => `${l.width}x${l.height}`).join(', ')}]`);
            }
            else if (wantsPortrait && !cameraIsPortrait) {
                infoLog?.log(
                    `Warmup: camera returned landscape despite portrait request — ` +
                    `keeping ladder portrait, normalize will cover-crop. ` +
                    `Camera=${this.cameraWidth}x${this.cameraHeight}, ` +
                    `ladder top=${ladder[ladder.length - 1].width}x${ladder[ladder.length - 1].height}`);
            }
            const ladderTop = ladder[ladder.length - 1];
            this.warmupTopSize = { width: ladderTop.width, height: ladderTop.height };
            // Cache the full ladder so openGate just opens the gate (+ ramps) —
            // no ladder rebuild, no index remap. Warmup encodes only L0; layers
            // stays null (single active tier = L0 via resolveActiveLadder).
            this.fullLayerLadder = [...ladder];
            this.layers = null;
            const warmupActive = [ladder[0]];

            void this.blazorRef.invokeMethodAsync(
                'OnTrackSettings',
                trackSettings.deviceId ?? null,
                trackSettings.facingMode ?? null);

            track.onended = () => {
                infoLog?.log('Warmup camera track ended externally — stopping recording');
                void this.stopRecording();
            };

            this.ensureWorker();
            await this.startWorker(warmupActive, /*initialGateOpen*/ false);

            this.isRecording = true;
            // State stays 'warming-up' until openGate flips it to 'recording'.
            infoLog?.log('Warmup pipeline running (wire gate closed)');
        } catch (error) {
            this.setRecordingState('error');
            errorLog?.log('Failed to start warmup:', error);
            const message = await this.describeStartError(error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', message);
        }
    }

    // Modal-to-live transition: expand the active ladder from warmup's single L0
    // tier up to the soft-start count, flip the wire gate open, and force a
    // keyframe so the first chunk reaching wireSend bootstraps the stream cleanly.
    // Warmup's L0 encoder keeps running (index 0 stays L0); only higher tiers are
    // appended. No fresh capture, no fresh HW slot.
    public async openGate(maxLayerCount = 3): Promise<void> {
        if (this._recordingState !== 'warming-up') {
            warnLog?.log(`openGate: not in warmup state (state=${this._recordingState})`);
            return;
        }
        if (!this.worker) {
            warnLog?.log('openGate: worker missing');
            return;
        }
        const isH264 = getCodecCategory(this.currentCodecString) === 'h264';
        // Build the FULL ladder up to the device/codec ceiling so the QC bump-
        // quality ramp can later add the top tier — capped to the available layer
        // count (desktop non-H264 = up to 4, adds 1080 only when that layer
        // exists; H264 = 3 ≤720; mobile = 2). Capture matches the ceiling top.
        const tierCeiling = DeviceInfo.isMobile
            ? 2
            : Math.min(isH264 ? 3 : 4, VIDEO.cameraLayerBaseBitratesKbps.length);
        this.currentMaxLayerCount = maxLayerCount;

        // Reuse the warmup ladder's top dims so the encoder keeps its
        // post-orientation-flip resolution. Falls back to camera dims (then
        // device defaults) only if warmup didn't record a ladder.
        // 0 means unset for cameraWidth/Height — || (not ??) is intentional
        /* eslint-disable @typescript-eslint/prefer-nullish-coalescing */
        const topW = this.warmupTopSize?.width
            || this.cameraWidth
            || (DeviceInfo.isMobile ? 640 : 1280);
        const topH = this.warmupTopSize?.height
            || this.cameraHeight
            || (DeviceInfo.isMobile ? 360 : 720);
        /* eslint-enable @typescript-eslint/prefer-nullish-coalescing */
        const fullLadder = this.withCodecBitrates(buildLadder({
            topWidth: topW,
            topHeight: topH,
            tierCount: tierCeiling,
            maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
            bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
            ...(tierCeiling >= 4 ? { tierSizes: CAMERA_TIER_SIZES } : {}),
        }), this.currentCodecString);
        this.fullLayerLadder = fullLadder.length > 0 ? [...fullLadder] : null;

        // Soft-start: open at the QC's current (soft) target, NOT the ceiling, so
        // the encoder isn't hammered with the top tier on start. The top tier is
        // added later by the bump-quality ramp (drain-rate / speculative probe /
        // good bandwidth) once the lower tiers run smoothly. receiverLayerCap is
        // reset so a viewer's demand re-applies via setMaxLayerId and is min()'d
        // against the health cap — demand can't pull the top tier in early.
        const softCount = Math.max(1, Math.min(maxLayerCount, tierCeiling));
        this.healthLayerCap = softCount;
        this.receiverLayerCap = Number.MAX_SAFE_INTEGER;
        const activeLadder = (this.fullLayerLadder && softCount >= 2)
            ? this.fullLayerLadder.slice(0, softCount)
            : null;
        this.layers = activeLadder;
        infoLog?.log(
            `openGate: full ladder [${fullLadder.map(l => `${l.width}x${l.height}`).join(', ')}], ` +
            `soft-start ${activeLadder?.length ?? 1} of ${fullLadder.length} tier(s)`);

        if (activeLadder && activeLadder.length >= 2) {
            // Hot-apply: warmup's L0 encoder keeps running at index 0; the higher
            // tiers spin up on the next captured frame (encode reconciles by
            // config, so no index remap).
            const encoderConfigs = this.toEncoderConfigs(activeLadder);
            try {
                await this.worker.reconfigureLayers(encoderConfigs);
            } catch (e) {
                warnLog?.log('openGate: reconfigureLayers failed — continuing at 1 tier:', e);
            }
        } else {
            // Soft-start floor = a single L0 tier. Warmup already encodes L0, so
            // this reconfigure is normally a no-op; kept so a maxLayerCount=1 open
            // still pins the bottom tier explicitly.
            const bottomLadder = this.resolveActiveLadder();
            if (bottomLadder.length > 0) {
                const encoderConfigs = this.toEncoderConfigs(bottomLadder);
                try {
                    await this.worker.reconfigureLayers(encoderConfigs);
                } catch (e) {
                    warnLog?.log('openGate: single-tier reconfigure failed — continuing at warmup tier:', e);
                }
            }
        }

        await this.worker.setGateOpen(true);
        await this.worker.requestKeyframe();

        this.setRecordingState('recording');
        await this.blazorRef.invokeMethodAsync('OnRecordingStarted');
        infoLog?.log(`openGate: live (${this.layers?.length ?? 1} layer(s), codec=${this.currentCodecString})`);
    }

    // Tear down a running warmup without firing OnRecordingStarted —
    // modal closed before the user clicked Join. Internally identical to
    // stopRecording; kept as a separate name so the C# side can distinguish
    // "warmup cancelled" from "live recording stopped" in its callbacks.
    public async cancelWarmup(): Promise<void> {
        if (this._recordingState !== 'warming-up') {
            return;
        }
        infoLog?.log('cancelWarmup: tearing down warmup pipeline');
        await this.stopRecording();
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
        // Mobile maxes out at 2 spatial tiers (640×360 top). Phones can't usefully
        // encode + ship a 720p+ top layer alongside lower tiers on a wireless
        // link, and the extra GPU work strangles their HW encoders. Desktop tops
        // at up to 4 tiers (adds 1080 when that layer exists); the 1080 top is
        // reserved for non-H264 (gated below at the probe + fallback), realized by
        // the QC ramp. Capped to the available layer count.
        const tierCeiling = DeviceInfo.isMobile
            ? 2
            : Math.min(4, VIDEO.cameraLayerBaseBitratesKbps.length);
        const tierCap = Math.max(1, Math.min(maxLayerCount, tierCeiling));
        infoLog?.log(`Starting video recording... audienceCodecs=[${audienceCodecs?.join(', ') ?? '(none)'}], maxLayerCount=${maxLayerCount} → tierCap=${tierCap}`);

        try {
            const cameraTopByTier: Record<number, Size> = {
                1: { width: 320, height: 180 },
                2: { width: 640, height: 360 },
                3: { width: 1280, height: 720 },
                4: { width: 1920, height: 1080 },
            };
            // Detect orientation BEFORE camera request so we ask for the
            // matching aspect directly — avoids the browser center-cropping a
            // 16:9 band out of a portrait sensor (the "head-only" crop).
            // Skip for iOS: iOS Safari MSTP doesn't auto-rotate and prefers
            // the camera's native landscape orientation; rotation is set on
            // the wire instead of baked into pixels.
            const wantsPortrait = DeviceInfo.isMobile
                && !DeviceInfo.isIos
                && ScreenOrientation.isObserved
                && ScreenOrientation.isPortrait;
            const baseTop: Size = cameraTopByTier[tierCap];
            const targetSize: Size = wantsPortrait
                ? { width: baseTop.height, height: baseTop.width }
                : baseTop;
            infoLog?.log(
                `Orientation pre-capture: isMobile=${DeviceInfo.isMobile}, ` +
                `isIos=${DeviceInfo.isIos}, ` +
                `screen.isObserved=${ScreenOrientation.isObserved}, ` +
                `screen.isPortrait=${ScreenOrientation.isPortrait}, ` +
                `wantsPortrait=${wantsPortrait}, ` +
                `requestTarget=${targetSize.width}x${targetSize.height}`);
            const targetFramerate = VIDEO.frameRate;
            this.currentFramerate = targetFramerate;

            const supportedCodecs = await detectSupportedCodecs(targetSize.width, targetSize.height);
            this.supportedCodecs = supportedCodecs;
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);
            infoLog?.log(`Supported encoder categories: [${this.supportedEncoderCategories.join(', ')}]`);

            const initialPick = this.pickInitialCodec(supportedCodecs, audienceCodecs, targetSize);
            // 4-tier uses the explicit append ladder (180/360/720/1080); reset to
            // undefined on any fallback that drops the 1080 top.
            let ladderTierSizes: readonly { width: number; height: number }[] | undefined =
                tierCap >= 4 ? CAMERA_TIER_SIZES : undefined;
            const ladderTop = buildLadder({
                topWidth: targetSize.width,
                topHeight: targetSize.height,
                tierCount: tierCap,
                maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
                bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
                ...(ladderTierSizes ? { tierSizes: ladderTierSizes } : {}),
            });
            // Fallback chain for HW encoder availability:
            //   1. Tier-cap (3 desktop / 2 mobile) at top resolution, prefer-hardware
            //   2. (Desktop only) Drop to 2-tier @ 360p, prefer-hardware
            //   3. Last resort: 1-tier at top resolution, no-preference
            //      (lets browser pick SW when HW encoder activation is failing
            //      — e.g. AMD iGPU + Windows MFT hitting concurrent-session
            //      limits or 0x8007000E "Not enough memory resources").
            // chosenHwAccel is plumbed into the worker config so the runtime
            // encoder matches the probed config; otherwise the probe says
            // "works with no-preference" but runtime keeps using prefer-hardware.
            let bestCodecString = await this.pickSimulcastCodec(
                supportedCodecs, audienceCodecs, ladderTop, 'prefer-hardware', false,
                tierCap >= 4 ? 'h264' : undefined);
            let ladder: LayerConfig[] = ladderTop;
            let chosenHwAccel: HardwareAcceleration = 'prefer-hardware';
            // 4-tier (1080 top) is reserved for non-H264 codecs. If no efficient
            // codec passed, drop the 1080 top and retry a 3-tier @720 ladder with
            // H264 allowed.
            if (!bestCodecString && tierCap >= 4) {
                const ladder3 = buildLadder({
                    topWidth: 1280,
                    topHeight: 720,
                    tierCount: 3,
                    maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
                    bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
                });
                infoLog?.log('4-tier 1080 probe failed for all non-H264 codecs — falling back to 3-tier @720 (H264 allowed)');
                const codec3 = await this.pickSimulcastCodec(
                    supportedCodecs, audienceCodecs, ladder3);
                if (codec3) {
                    bestCodecString = codec3;
                    ladder = ladder3;
                    ladderTierSizes = undefined;
                }
            }
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
                    ladder = ladder2;
                    ladderTierSizes = undefined;
                }
            }
            if (!bestCodecString) {
                // Last-resort: 1-tier at the original top resolution with
                // no-preference. Excludes failed codecs on this attempt so
                // server-driven codec switches won't pick a proven-broken
                // codec.
                const ladder1 = buildLadder({
                    topWidth: targetSize.width,
                    topHeight: targetSize.height,
                    tierCount: 1,
                    maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
                    bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
                });
                infoLog?.log(`Tier-cap and drop-top probes failed — falling back to 1-tier @ ${targetSize.width}x${targetSize.height} with hardwareAcceleration='no-preference'`);
                const codec1 = await this.pickSimulcastCodec(
                    supportedCodecs, audienceCodecs, ladder1, 'no-preference', /*excludeOnFail*/ true);
                if (codec1) {
                    bestCodecString = codec1;
                    ladder = ladder1;
                    chosenHwAccel = 'no-preference';
                    ladderTierSizes = undefined;
                } else {
                    warnLog?.log(
                        `All probe attempts failed (initialPick=${initialPick}) — ` +
                        `aborting startRecording, HW+SW encoders appear unavailable`);
                    throw new Error(USER_FACING_RESTART_MESSAGE);
                }
            }
            this.currentHardwareAcceleration = chosenHwAccel;
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);
            const codecCategory = getCodecCategory(bestCodecString);
            ladder = buildLadder({
                topWidth: ladder[ladder.length - 1].width,
                topHeight: ladder[ladder.length - 1].height,
                tierCount: ladder.length,
                maxTierCount: VIDEO.cameraLayerBaseBitratesKbps.length,
                bitratesKbps: VIDEO.cameraLayerBaseBitratesKbps,
                ...(ladderTierSizes ? { tierSizes: ladderTierSizes } : {}),
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

            // Safety flip — only when we did NOT pre-decide orientation
            // (iOS path, or rare case where ScreenOrientation wasn't
            // observable yet). When pre-decision happened (wantsPortrait),
            // the ladder reflects the USER's intent (portrait); if the
            // camera ignored our portrait request and delivered landscape,
            // we must NOT flip the ladder back to landscape — that would
            // ship landscape video against the user's intent. Instead leave
            // ladder portrait so `normalizeFrame` cover-crops the landscape
            // source into a portrait target.
            const cameraIsPortrait = this.cameraHeight > this.cameraWidth;
            const ladderTopIsPortrait = ladder[ladder.length - 1].height > ladder[ladder.length - 1].width;
            if (!wantsPortrait && cameraIsPortrait !== ladderTopIsPortrait) {
                ladder = ladder.map(l => ({ ...l, width: l.height, height: l.width }));
                this.layers = ladder.length >= 2 ? [...ladder] : null;
                this.fullLayerLadder = this.layers ? [...this.layers] : null;
                infoLog?.log(`Camera orientation mismatch — flipped ladder to: [${ladder.map(l => `${l.width}x${l.height}`).join(', ')}]`);
            } else if (wantsPortrait && !cameraIsPortrait) {
                infoLog?.log(`Camera returned landscape despite portrait request — keeping ladder portrait, normalize will cover-crop. Camera=${this.cameraWidth}x${this.cameraHeight}, ladder top=${ladder[ladder.length - 1].width}x${ladder[ladder.length - 1].height}`);
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

            // The screen track is pre-acquired by ScreenShareGesture inside the DOM
            // click handler (getDisplayMedia needs transient activation, which the
            // Blazor server round-trip to here has already consumed). captureScreenCast
            // returns that gesture-acquired track.
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
            this.healthLayerCap = Number.MAX_SAFE_INTEGER;
            this.receiverLayerCap = Number.MAX_SAFE_INTEGER;
            this.warmupTopSize = null;
            this.lastCodecSwitchAt = 0;
            this.startedAtMs = 0;
            this.softwareFallbackEngaged = false;
            this.currentHardwareAcceleration = 'prefer-hardware';
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
        // SW fallback is sticky: don't let a server-driven codec switch pull us
        // back into a wedged HW codec (H.264 is always in the audience set).
        if (this.softwareFallbackEngaged) return;

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

    /**
     * Server-driven cap on the encoder ladder: the recorder shouldn't waste
     * encode time on layers that no subscriber is currently asking for.
     *
     * `maxLayerId` is the aggregate max of `ReceiveQuality.LayerId` across
     * all subscribers, as reported by
     * `LiveVideoStreams.MaxRequestedLayerId`. -1 == nobody is currently
     * subscribed (or every viewer is paused) — we leave the ladder alone in
     * that case so the next joiner doesn't pay a restart cost.
     */
    public setMaxLayerId(maxLayerId: number): void {
        this.lastMaxLayerId = maxLayerId;
        this.applyTargetFps();

        if (maxLayerId < 0)
            return;
        // Receiver demand sets a ceiling on the active tiers, but never pulls in a
        // tier the sender's health cap hasn't cleared — applyEffectiveLayers mins
        // the two. So the top (e.g. 1080) tier is added only when the health ramp
        // AND a viewer both want it.
        this.receiverLayerCap = maxLayerId + 1;
        this.applyEffectiveLayers();
    }

    // Local voice-activity edge from the audio recorder's VAD. Speaking forces
    // full fps (the active speaker must stay smooth even when unfocused); on
    // silence we hold full rate briefly through natural pauses before letting
    // demand-driven pacing resume.
    public setSpeaking(isSpeaking: boolean): void {
        if (isSpeaking) {
            if (this.speakingHoldTimer !== null) {
                clearTimeout(this.speakingHoldTimer);
                this.speakingHoldTimer = null;
            }
            const changed = !this.isSpeaking;
            this.isSpeaking = true;
            if (changed)
                this.applyTargetFps();
        }
        else if (this.isSpeaking && this.speakingHoldTimer === null) {
            this.speakingHoldTimer = setTimeout(() => {
                this.speakingHoldTimer = null;
                this.isSpeaking = false;
                this.applyTargetFps();
            }, VAD_FPS_HOLD_MS);
        }
    }

    // Temporal demand → encoder fps. Speaking → full rate. Otherwise the
    // highest requested layer's tier drives it (focused top → full, lower
    // tiers → fewer fps — the CPU win for non-focused streams). Only once live
    // (warmup/preview must run full-rate so the encoder proves itself and the
    // self-view stays smooth). maxLayerId < 0 is left running, NOT idle-stopped:
    // it's ambiguous (no subscriber vs all-paused vs a join transient vs
    // node-local conservative) and would starve an actively-watched stream.
    private applyTargetFps(): void {
        if (this._recordingState !== 'recording')
            return;
        if (this.isSpeaking) {
            this.lastTargetFps = VIDEO.frameRate;
            void this.worker?.setTargetFps(VIDEO.frameRate);
            return;
        }
        if (this.lastMaxLayerId >= 0) {
            const fps = fpsForRequestedLayer(this.fullLayerLadder, this.lastMaxLayerId, VIDEO.frameRate);
            this.lastTargetFps = fps;
            void this.worker?.setTargetFps(fps);
        }
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
        // Prefer the current-window mean (live encoder load after a prune);
        // fall back to the lifetime mean until the first delta window lands.
        const lifetimeMeanEncodeTimeMs = liveStats && liveStats.encodeTimeMsCount > 0
            ? liveStats.encodeTimeMsSum / liveStats.encodeTimeMsCount
            : 0;
        const lifetimeMeanMaxLayerEncodeTimeMs = liveStats && liveStats.encodeTimeMsCount > 0
            ? liveStats.encodeTimeMsMaxSum / liveStats.encodeTimeMsCount
            : 0;
        const meanEncodeTimeMs = this.windowMeanEncodeTimeMs >= 0
            ? this.windowMeanEncodeTimeMs
            : lifetimeMeanEncodeTimeMs;
        const meanMaxLayerEncodeTimeMs = this.windowMeanMaxLayerEncodeTimeMs >= 0
            ? this.windowMeanMaxLayerEncodeTimeMs
            : lifetimeMeanMaxLayerEncodeTimeMs;
        // Current-window encoded bitrate (sum over CURRENTLY encoding layers),
        // not the lifetime average — a focus→unfocus prune drops the encoder to
        // one layer, and the lifetime `bytesEncoded/duration` would keep showing
        // the focused-era sum and read as "still encoding all layers".
        const aggregateBitrateKbps = (this.bytesPerSec * 8) / 1000;

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
                })),
            } : null,
            dropTraceByStage,
            // Demand inputs that drive layer pruning + fps pacing. activeLayerCount
            // is the REAL number of encoders running (min of all caps), which
            // differs from the C# health-cap "effective" when receiver demand is
            // the binding cap. receiverLayerCap = aggregate max requested layer + 1
            // across all viewers (MAX_SAFE = unset / nobody asked yet).
            activeLayerCount: this.layers?.length ?? (this.fullLayerLadder ? 1 : 0),
            receiverLayerCap: this.receiverLayerCap === Number.MAX_SAFE_INTEGER ? -1 : this.receiverLayerCap,
            healthLayerCap: this.healthLayerCap === Number.MAX_SAFE_INTEGER ? -1 : this.healthLayerCap,
            lastMaxLayerId: this.lastMaxLayerId,
            targetFps: this.lastTargetFps,
            isSpeaking: this.isSpeaking,
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

        if (this.speakingHoldTimer !== null) {
            clearTimeout(this.speakingHoldTimer);
            this.speakingHoldTimer = null;
        }
        this.isSpeaking = false;
        this.lastMaxLayerId = -1;
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
            onPreviewFrame: frame => this.handlePreviewFrame(frame),
            onPreviewFramePresentation: presentation => this.setPreviewFramePresentation(presentation),
            onPreviewTrackReady: track => this.handleWorkerPreviewTrack(track),
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

    private async startWorker(ladder: LayerConfig[], initialGateOpen = true): Promise<void> {
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
        }

        // Safari: MSTP is worker-only, so main can't build it. Transfer a CLONE
        // of the camera track into the worker and let it build the processor in
        // its realm — source-bound capture, no main-thread rVFC tick. A clone
        // keeps the attempt non-destructive: main retains `inputTrack` for the
        // rVFC fallback and for restarts (which re-clone).
        if (!useMstp) {
            try {
                const clone = this.inputTrack.clone();
                const ok = await this.worker.setSourceTrack(clone);
                if (ok) {
                    this.workerSourceCancelled = false;
                    useMstp = true;
                    infoLog?.log('startWorker: capture path = worker MSTP (transferred clone)');
                } else {
                    warnLog?.log('startWorker: worker has no MSTP, falling back to rVFC pump');
                }
            } catch (e) {
                warnLog?.log('startWorker: worker MSTP attempt failed, falling back to rVFC pump:', e);
            }
        }

        if (!useMstp) {
            infoLog?.log('startWorker: MSTP unavailable (main + worker), using rVFC pump');
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
            let lastPumpTickAtMs = performance.now();
            const onFrame = (now: DOMHighResTimeStamp, metadata: VideoFrameCallbackMetadata): void => {
                if (this.workerSourceCancelled) return;
                void now;
                lastPumpTickAtMs = performance.now();

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
                // `rpcNoWait` returns immediately after postMessage transfers
                // the frame, so the next rVFC tick fires without waiting for
                // the worker ack. Without this the round-trip easily exceeds
                // the 33 ms frame interval on Android and caps capture at
                // half-rate. Worker absorbs bursts in its ingress queue.
                try {
                    void workerForPump.pushFrame(frame, rpcNoWait);
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

        // Display ceiling `normalize` targets — the full-ladder top, NOT the
        // active ladder top — so the self-preview stays full-res even when the
        // active encode ladder is just L0. `spatialize` downscales from it.
        const ceilingTier = this.fullLayerLadder?.[this.fullLayerLadder.length - 1]
            ?? this.warmupTopSize
            ?? ladder[ladder.length - 1];
        const normalizeSize = { width: ceilingTier.width, height: ceilingTier.height };

        const framerate = this.requireFramerate('startWorker');
        const isFrontCamera = this.inputTrack.getSettings().facingMode === 'user';
        const config: WireSafeRecorderConfig = {
            chatId: this.chatId,
            apiUrl,
            sourceKind: this.currentMode === 'screen' ? 1 : 0,
            isFrontCamera,
            isIos: DeviceInfo.isIos || BrowserInfo.appKind === 'Ios',
            encoderConfigs,
            normalizeSize,
            downscalerMode: getDownscalerMode(),
            // Both camera and screencast use a 3s keyframe cadence.
            keyframeIntervalFrames: framerate * 3,
            maxKeyFrameIntervalMs: 3000,
            keepAlivePeriodMs: this.currentMode === 'screen' ? VIDEO.screenCastKeepAlivePeriodMs : 0,
            hardwareAcceleration: this.currentHardwareAcceleration,
            initialGateOpen,
        };

        const sourceStartedAtMs = Date.now();
        // `sourceStartedAtMs` is the per-run wire stream reference (resets on
        // every restart — wire stream is fresh per run). `this.startedAtMs`
        // tracks the recording SESSION start for the diagnostics Duration row;
        // it must survive setLayers-driven restartWithCurrentConfig() so the
        // user sees continuous duration. Only the explicit stopRecording()
        // path resets it back to 0.
        if (this.startedAtMs === 0)
            this.startedAtMs = sourceStartedAtMs;
        // `start()` resolves only when the run finishes draining (per
        // the new contract) — fire and forget so we can return from
        // startRecording() and let the operator pipe drive itself.
        const previousPreviewTrack = this.previewTrack;
        const previewGenerator = this.createGeneratedPreviewTrack();
        // No main-side generator (Safari): ask the worker to build one in its
        // realm and ship the track back via onPreviewTrackReady. Leave the
        // preview pending (don't commit to canvas yet) until that arrives.
        const createPreviewInWorker = !previewGenerator;
        if (previewGenerator) {
            this.setPreviewFramePresentation(null);
            this.previewCanvasFallback = false;
            this.previewTrack = previewGenerator.track;
            if (this.previewTrack !== previousPreviewTrack)
                this.notifyPreviewTrackChanged();
        } else {
            this.setPreviewFramePresentation(null);
        }

        void this.worker.start(
            { sourceStartedAtMs, config, createPreviewInWorker },
            previewGenerator?.writable,
        ).catch((e: unknown) => {
            errorLog?.log('Worker start rejected:', e);
            if (previewGenerator && this.previewTrack === previewGenerator.track) {
                this.cleanupGeneratedPreviewTrack();
                this.previewCanvasFallback = true;
                this.previewTrack = null;
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

        // Once SW fallback is engaged, never re-pick — a fresh detect would
        // resurface the wedged HW codec. Stay on SW H.264 and just retry.
        if (this.softwareFallbackEngaged) {
            this.scheduleRecovery(reason);
            return;
        }

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

    // Recovers a reclaimed encoder / wedged source the frame-driven path can't
    // see: a foreground capture flatline means no frame reaches the dead
    // encoder to throw, so force a restart. Foreground-gated to avoid churning
    // a legitimately backgrounded idle encoder into a restart loop.
    private detectCaptureStall(
        stats: RecorderStats,
        previous: RecorderStats | null,
        nowMs: number,
    ): void {
        const track = this.inputTrack;
        const sourceShouldRun = this._recordingState === 'recording'
            && !stats.isTabBackgrounded
            && track !== null && track.readyState === 'live' && !track.muted;
        if (!sourceShouldRun || previous === null || this.recoveryScheduled
            || stats.framesCaptured > previous.framesCaptured) {
            this.captureStallSinceMs = 0;
            return;
        }
        if (this.captureStallSinceMs === 0) {
            this.captureStallSinceMs = nowMs;
            return;
        }
        if (nowMs - this.captureStallSinceMs >= CAPTURE_STALL_RECOVERY_MS) {
            this.captureStallSinceMs = 0;
            this.scheduleRecovery('foreground capture stalled (encoder likely reclaimed)');
        }
    }

    private scheduleRecovery(reason: string): void {
        if (this.recoveryScheduled || !this.isRecording || this.disposed)
            return;

        this.recoveryScheduled = true;
        this.recoveryAttempts++;
        // recoveryAttempts is reset to 0 by the recorder-health monitor as soon
        // as a successful bundle ships (see ~line 1557). Crossing the cap means
        // every attempt since the last success has failed — surface a fatal
        // error to the user instead of looping forever.
        if (this.recoveryAttempts > MAX_RECOVERY_ATTEMPTS) {
            warnLog?.log(
                `scheduleRecovery: giving up after ${this.recoveryAttempts - 1} ` +
                `consecutive failed attempts (last reason: ${reason}; ` +
                `current codec=${this.currentCodecString})`);
            this.recoveryScheduled = false;
            void this.engageEncoderFallback(reason).then(engaged => {
                if (!engaged)
                    void this.blazorRef.invokeMethodAsync('OnRecordingError', USER_FACING_RESTART_MESSAGE);
            });
            return;
        }
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

    // Graded last-resort escalation when normal HW recovery is exhausted
    // (GPU reset wedges the encode block — the same event that kills WebGL
    // contexts — yet isConfigSupported still reports the HW codec, so plain
    // re-pick loops). Desktop-only. Two tiers, one per exhaustion:
    //   1. Still on a non-H.264 codec (HEVC wedged, possibly via 'codec hang'
    //      errors that never excluded it): drop that category and retry
    //      HARDWARE H.264 — every viewer decodes H.264.
    //   2. Already on H.264 and still failing: drop to prefer-software H.264
    //      (OpenH264, independent of the GPU). No software HEVC exists in the
    //      browser, so HEVC always degrades through H.264, never to SW HEVC.
    // Returns false only when nothing is left to try (mobile, SW already
    // engaged, or SW H.264 fails to probe) so the caller surfaces the fatal
    // restart message.
    private async engageEncoderFallback(reason: string): Promise<boolean> {
        if (DeviceInfo.isMobile || this.softwareFallbackEngaged || !this.isRecording || this.disposed)
            return false;

        const currentCategory = getCodecCategory(this.currentCodecString);
        if (currentCategory !== 'h264') {
            excludeEncoderCodec(currentCategory);
            this.recoveryAttempts = 0;
            infoLog?.log(
                `engageEncoderFallback: ${currentCategory} wedged (${reason}) → ` +
                `excluding it, trying hardware H.264 before software`);
            void this.repickCodecAndRestart(`${currentCategory} wedged → hardware H.264`);
            return true;
        }

        // Software simulcast is CPU-heavy, so cap to the two lowest tiers
        // (≤640×360), built fresh from the mode's base bitrates — NOT sliced
        // from the live ladder, which a prior restart may have already
        // collapsed to a single high tier (then the slice would keep 720p in
        // software, the exact load we're avoiding).
        const swTopWidth = 640;
        const swTopHeight = 360;
        const baseBitratesKbps = this.currentMode === 'screen'
            ? VIDEO.screenCastLayerBaseBitratesKbps
            : VIDEO.cameraLayerBaseBitratesKbps;
        const h264Codec = getSoftwareH264Codec(swTopWidth, swTopHeight);
        const reduced = this.withCodecBitrates(buildLadder({
            topWidth: swTopWidth,
            topHeight: swTopHeight,
            tierCount: 2,
            maxTierCount: baseBitratesKbps.length,
            bitratesKbps: baseBitratesKbps,
        }), h264Codec);
        if (reduced.length === 0)
            return false;

        const probe = await probeEncoder(h264Codec, reduced, undefined, undefined, 'prefer-software');
        if (!probe.supported) {
            warnLog?.log(
                `engageEncoderFallback: SW H.264 probe failed (${h264Codec}) — ` +
                `no software fallback available`);
            return false;
        }

        this.softwareFallbackEngaged = true;
        this.currentCodecString = h264Codec;
        this.currentCodecHardwareAccel = false;
        this.currentHardwareAcceleration = 'prefer-software';
        this.fullLayerLadder = reduced;
        this.layers = reduced.length >= 2 ? [...reduced] : null;
        // Let QC re-evaluate the fresh low-tier ladder, not a prior collapsed cap.
        this.healthLayerCap = Number.MAX_SAFE_INTEGER;
        this.recoveryAttempts = 0;
        infoLog?.log(
            `engageEncoderFallback: hardware H.264 wedged (${reason}) → software H.264 ` +
            `(${h264Codec}), ${reduced.length}-tier ladder ` +
            `[${reduced.map(l => `${l.width}x${l.height}`).join(', ')}]`);
        this.scheduleRecovery('software fallback');
        return true;
    }

    // `this.layers === null` is the "single-encoder mode" sentinel set
    // by `setLayers` when QC targets ≤1 layer. Previously both restart
    // paths fell back to `this.fullLayerLadder`, which silently restarted
    // at the FULL 3-tier 1280x720 ladder — the exact opposite of what
    // QC wanted, turning every "demote to 1" into an "escalate to max"
    // and snowballing encoder degradation. In single-encoder mode the
    // restart must run with only the bottom layer.
    private resolveActiveLadder(): LayerConfig[] {
        if (this.layers) return this.layers;
        if (this.fullLayerLadder && this.fullLayerLadder.length > 0)
            return [this.fullLayerLadder[0]];
        return [];
    }

    private async recoverNow(): Promise<void> {
        if (!this.worker || !this.inputTrack) {
            warnLog?.log('recoverNow: worker or input track missing — skipping');
            return;
        }
        const ladder = this.resolveActiveLadder();
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

        // Preserve the gate-closed warmup state across codec-switch
        // restarts; without this, a warmup-time encoder failure would
        // recover into a live stream.
        const initialGateOpen = this._recordingState !== 'warming-up';
        await this.startWorker(ladder, initialGateOpen);
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
        const ladder = this.resolveActiveLadder();
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
        const initialGateOpen = this._recordingState !== 'warming-up';
        await this.startWorker(ladder, initialGateOpen);
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
        return {
            width: layer.width,
            height: layer.height,
            baseBitrateKbps,
            bitrateKbps: layer.bitrateKbps ?? baseBitrateKbps,
        };
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
    private static readonly EncodeDeficitEmaAlpha = 0.3;
    private encodeDeficitEma = 0;
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
    // Current-window mean encode time (per bundle / per slowest layer), so the
    // header reflects the live encoder load after a layer prune rather than the
    // lifetime mean. -1 until the first delta window produces samples.
    private windowMeanEncodeTimeMs = -1;
    private windowMeanMaxLayerEncodeTimeMs = -1;
    private windowMeanDownscaleTimeMs = -1;
    private windowDownscaleTimeMsMax = -1;
    private readonly dropPerSec = new Map<number, number>();
    private lastReportTickMs = 0;

    private startRecorderHealthMonitor(): void {
        this.stopRecorderHealthMonitor();
        this.lastRecorderHealthStats = null;
        this.lastRecorderHealthWasPeerConnected = false;
        this.encodeDeficitEma = 0;
        this.senderDropRatioEma = 0;
        this.capturedPerSec = 0;
        this.bundlesPerSec = 0;
        this.bytesPerSec = 0;
        this.windowMeanEncodeTimeMs = -1;
        this.windowMeanMaxLayerEncodeTimeMs = -1;
        this.windowMeanDownscaleTimeMs = -1;
        this.windowDownscaleTimeMsMax = -1;
        this.dropPerSec.clear();
        this.lastReportTickMs = 0;
        this.captureStallSinceMs = 0;
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
            // Main thread owns the `document` reference — stamp the flag here
            // so the worker doesn't need a document poke. The classifier reads
            // it to relax encode-ratio thresholds under background-tab Chrome
            // throttling.
            stats.isTabBackgrounded =
                typeof document !== 'undefined' && document.visibilityState === 'hidden';
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
                    const encCountDelta = Math.max(0, stats.encodeTimeMsCount - previous.encodeTimeMsCount);
                    if (encCountDelta > 0) {
                        this.windowMeanEncodeTimeMs =
                            Math.max(0, stats.encodeTimeMsSum - previous.encodeTimeMsSum) / encCountDelta;
                        this.windowMeanMaxLayerEncodeTimeMs =
                            Math.max(0, stats.encodeTimeMsMaxSum - previous.encodeTimeMsMaxSum) / encCountDelta;
                    }
                    const dsCountDelta = Math.max(0, stats.downscaleTimeMsCount - previous.downscaleTimeMsCount);
                    if (dsCountDelta > 0) {
                        this.windowMeanDownscaleTimeMs =
                            Math.max(0, stats.downscaleTimeMsSum - previous.downscaleTimeMsSum) / dsCountDelta;
                        this.windowDownscaleTimeMsMax = stats.downscaleTimeMsMax;
                    }
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

            this.detectCaptureStall(stats, previous, nowMs);

            // Drop trace deltas → senderFrameDropRatio. Sum only sender
            // stages (1..30). Denominator = bundles attempted = bundles
            // shipped + bundles dropped in the sender pipeline.
            let senderDropsDelta = 0;
            if (previous && isPeerConnected && this.lastRecorderHealthWasPeerConnected) {
                for (const [stage, count] of stats.dropTrace) {
                    const stageNum = stage as number;
                    // SenderFpsPacing (5) is intentional temporal downsampling,
                    // not loss — exclude it so demand-driven fps pacing doesn't
                    // read as an uplink drop.
                    if (stageNum < 1 || stageNum > 30 || stageNum === 5) continue;
                    const prevCount = previous.dropTrace.get(stage) ?? 0;
                    senderDropsDelta += Math.max(0, count - prevCount);
                }
                const shippedDelta = Math.max(0, stats.bundlesShipped - previous.bundlesShipped);
                const totalProduced = shippedDelta + senderDropsDelta;
                const ratio = totalProduced > 0 ? senderDropsDelta / totalProduced : 0;
                this.senderDropRatioEma =
                    VideoRecorder.EncodeDeficitEmaAlpha * ratio
                    + (1 - VideoRecorder.EncodeDeficitEmaAlpha) * this.senderDropRatioEma;
            }

            // Encoder THROUGHPUT DEFICIT, 0..1. Window-derived ratio of
            // "bundles encoded this tick / frames OFFERED to encode this tick"
            // subtracted from 1 and EMA-smoothed. The denominator is
            // framesOffered (survivors of floodGate + temporalPace), not
            // framesCaptured — otherwise intentional fps pacing would register
            // as the encoder falling behind. A queue-full encoder still
            // emitting at the offered rate registers 0 here; deficit only grows
            // when encoder emit rate actually falls behind. QC uses this to
            // decide whether to demote a spatial layer.
            if (previous) {
                const encDelta = Math.max(0, stats.bundlesEncoded - previous.bundlesEncoded);
                const capDelta = Math.max(0, stats.framesOffered - previous.framesOffered);
                if (capDelta > 0) {
                    const throughputRatio = Math.min(1, encDelta / capDelta);
                    const deficit = 1 - throughputRatio;
                    this.encodeDeficitEma =
                        VideoRecorder.EncodeDeficitEmaAlpha * deficit
                        + (1 - VideoRecorder.EncodeDeficitEmaAlpha) * this.encodeDeficitEma;
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
                this.encodeDeficitEma,
                this.senderDropRatioEma,
                stats.wireLastAckAgeMs,
                isPeerConnected,
                stages,
                counts,
                stats.bundlesShipped,
                stats.bundlesEncoded,
                stats.bytesEncoded,
                stats.encodeQueueDepthEma,
                stats.wireQueueDepthEma,
                stats.floodGateSkipPerSec,
                stats.peerReconnectStreak,
                stats.encoderRestartStreakIn60s,
                stats.isTabBackgrounded,
                stats.wireAckedBytes,
                this.windowMeanEncodeTimeMs,
                this.windowMeanDownscaleTimeMs,
                this.windowDownscaleTimeMsMax,
                stats.keepAliveFramesInjected);
        } catch (e) {
            warnLog?.log('reportRecorderStats failed:', e);
        } finally {
            this.recorderHealthInFlight = false;
        }
    }

    private cleanupPreviewTrack(): void {
        this.cleanupGeneratedPreviewTrack();
        this.setPreviewFramePresentation(null);
        this.previewCanvasFallback = false;
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
        if (pickRenderBackendKind() !== 'mstg') {
            debugLog?.log('createGeneratedPreviewTrack: canvas preview selected');
            return null;
        }

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
            warnLog?.log('createGeneratedPreviewTrack failed; falling back to canvas preview:', e);
            this.cleanupGeneratedPreviewTrack();
        }
        return null;
    }

    private cleanupGeneratedPreviewTrack(): void {
        const track = this.generatedPreviewTrack;
        if (!track) return;
        try { track.stop(); } catch { /* ignore */ }
        this.generatedPreviewTrack = null;
        if (this.previewTrack === track) {
            this.previewCanvasFallback = false;
            this.previewTrack = null;
        }
        this.setPreviewFramePresentation(null);
    }

    private async handlePreviewFrame(frame: VideoFrame): Promise<void> {
        const pending: Promise<void>[] = [];
        try {
            for (const cb of this.previewFrameListeners) {
                try {
                    const result = cb(frame);
                    if (isPromiseLike(result)) {
                        pending.push(result.catch((e: unknown) => {
                            warnLog?.log('preview frame listener failed', e);
                        }));
                    }
                } catch (e) {
                    warnLog?.log('preview frame listener threw', e);
                }
            }
            if (pending.length > 0)
                await Promise.all(pending);
        } finally {
            try { frame.close(); } catch { /* already closed */ }
        }
    }

    private notifyPreviewTrackChanged(): void {
        for (const cb of this.stateChangeListeners) {
            try { cb(this._recordingState); } catch (e) { warnLog?.log('state change listener threw', e); }
        }
    }

    // Worker-created preview track (Safari, where MSTG/VTG are worker-only).
    // Non-null ⇒ attach it to the preview <video>; null ⇒ canvas fallback.
    private handleWorkerPreviewTrack(track: MediaStreamTrack | null): void {
        // The run may already have stopped by the time this arrives; the worker
        // owns the track's lifetime, so just stop and drop a stale one.
        if (this.disposed || !this.isRecording) {
            if (track) try { track.stop(); } catch { /* ignore */ }
            return;
        }
        if (track) {
            infoLog?.log(`Worker preview track ready (id=${track.id})`);
            this.previewCanvasFallback = false;
            this.previewTrack = track;
        } else {
            infoLog?.log('Worker preview track unavailable — using canvas fallback');
            this.previewCanvasFallback = true;
            this.previewTrack = null;
        }
        this.notifyPreviewTrackChanged();
    }

    private setPreviewFramePresentation(presentation: PreviewFramePresentation | null): void {
        const current = this.previewFramePresentation;
        if (current?.rotation === presentation?.rotation)
            return;

        this.previewFramePresentation = presentation;
        for (const cb of this.previewPresentationListeners) {
            try { cb(presentation); } catch (e) { warnLog?.log('preview presentation listener threw', e); }
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
        hardwareAcceleration: HardwareAcceleration = 'prefer-hardware',
        excludeOnFail = false,
        excludeCategory?: CodecInfo['category'],
    ): Promise<string | null> {
        // Live encoder probe at top-layer dims. Result cached per
        // (codec, dims, layerCount, hwAccel) so repeated picks across the
        // fallback chain (3-tier prefer-hardware → 2-tier prefer-hardware →
        // 1-tier no-preference) don't burn fresh HW slots. On FAIL probe
        // disposes the encoder in finally — failed codec category gets
        // excluded ONLY if excludeOnFail (last fallback) so earlier failures
        // don't prevent subsequent attempts from trying the same codec
        // under different config.
        for (const codecInfo of this.listCodecCandidatesByEfficiency(supportedCodecs, audienceCodecs)) {
            if (excludeCategory && codecInfo.category === excludeCategory)
                continue;
            const layersWithBitrates = this.withCodecBitrates(ladder, codecInfo.codec);
            const result = await probeEncoder(
                codecInfo.codec, layersWithBitrates, undefined, undefined, hardwareAcceleration);
            if (result.supported) {
                const top = layersWithBitrates[layersWithBitrates.length - 1];
                infoLog?.log(`pickSimulcastCodec: ${codecInfo.category} (${codecInfo.codec}) PASS @ ${top.width}x${top.height} (${ladder.length} layer(s)), hwAccel=${hardwareAcceleration}, median=${result.medianEncodeMs.toFixed(1)}ms`);
                return codecInfo.codec;
            }
            infoLog?.log(`pickSimulcastCodec: ${codecInfo.category} (${codecInfo.codec}) FAIL stage=${result.failedStage}, hwAccel=${hardwareAcceleration}`);
            if (excludeOnFail) {
                // Last-resort fallback also failed for this codec — exclude
                // it for the session so server-driven updateSupportedDecoderCodecs
                // won't later switch into a codec proven non-functional.
                // No-op for h264 (universal fallback) and for codecs proven
                // working this session.
                excludeEncoderCodec(codecInfo.category);
            }
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

import { getLogs } from 'logging';
import { Api, momentToSeconds, secondsToMoment, streamingApi, type VideoFrameDto } from 'api';
import { RunningEMA } from 'math';

const RPC_SESSION_DEFAULT = '~';
import { ServerClock } from 'server-clock';
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import { DocumentEvents } from 'event-handling';
import { Versioning } from 'versioning';
import { type EventHandler } from 'event-handling';
import { type Subscription } from 'rxjs';
import { renderQualityLevelForWidth } from './render-quality';
import type {
    DecoderWorker,
    DecoderWorkerLatencyReport,
} from '../../Services/Video/workers/decoder-worker-contract';
import type { DecoderConfig, DecoderStats } from '../../Services/Video/webcodecs-decoder';
import {
    getCodecCandidates,
    mapCodecToWebCodecs,
    selectDecoderCodec,
} from '../../Services/Video/hevc-codec-selection';
import {
    createInputChannel,
    type RawChunkMessage,
    type StreamEndpoints,
    supportsTransferableStreams,
} from '../../Services/Video/workers/stream-channel';
import type { RenderBackend } from './render-backend';
import { CanvasRenderBackend } from './render-backend-canvas';
import { OffThreadRenderBackend, isOffThreadPlausible } from './render-backend-mstg';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { ConnectivityUI } from '../../../UI.Blazor/Services/ConnectivityUI/connectivity-ui';
import { AC, VIDEO, whenAppConstantsReady } from 'app-constants';
import { OwnedArrayBufferTracker, ReplaceableSlot } from 'buffers';
import { SharedSettings } from 'shared-settings';
import { SharedSettingsWorkerSync } from 'shared-settings-worker';

// Backend selection: prefer the off-thread renderer wherever a generator API
// (MediaStreamTrackGenerator on Chromium, VideoTrackGenerator on Safari) is
// plausibly available. The worker probes the real APIs; if neither exists,
// it rejects setupWorker() and we swap to canvas at runtime.
// ?renderBackend=mstg|canvas overrides for diagnostics.
function pickRenderBackend(canvas: HTMLCanvasElement, videoEl: HTMLVideoElement): RenderBackend {
    let flag: string | null = null;
    try {
        flag = new URL(globalThis.location.href).searchParams.get('renderBackend');
    } catch { /* non-browser context */ }
    if (flag === 'canvas')
        return new CanvasRenderBackend(canvas);
    if (flag === 'mstg' || isOffThreadPlausible())
        return new OffThreadRenderBackend(videoEl);
    return new CanvasRenderBackend(canvas);
}

// Global registry of active VideoPlayer instances for diagnostics
const activePlayers = new Map<string, VideoPlayer>();
export function getActivePlayers(): ReadonlyMap<string, VideoPlayer> {
    return activePlayers;
}

const requestedReceiveQuality = new Map<string, { maxSpatialLayer: number; maxTemporalLayer: number } | null>();

export function recordRequestedReceiveQuality(
    streamId: string,
    quality: { maxSpatialLayer: number; maxTemporalLayer: number } | null
): void {
    if (quality === null)
        requestedReceiveQuality.delete(streamId);
    else
        requestedReceiveQuality.set(streamId, quality);
}

export interface RemoteStreamDiagnostics {
    streamId: string;
    authorId: string;
    codec: string;
    codecCategory: string;
    bitrateKbps: number;
    pipelineLatencyMs: number;
    jitterBufferMs: number;
    jitterEstimateMs: number;
    smoothedRttMs: number;
    rttGradientMs: number;
    playbackRate: number;
    bufferSize: number;
    receivedFrameCount: number;
    receivedKeyframeCount: number;
    renderFrameCount: number;
    skipToLiveCount: number;
    waitingForKeyframe: boolean;
    qualityReductionRequested: boolean;
    codecSlowTickCount: number;
    decoderStats: DecoderStats | null;
    avDriftMs: number | null;
    forwarded: {
        ForwardedSpatialLayerId: number;
        ForwardedWidth: number;
        ForwardedHeight: number;
        ObservedMaxSpatialLayer: number;
    } | null;
    requestedReceiveQuality: {
        maxSpatialLayer: number;
        maxTemporalLayer: number;
    } | null;
    streamAgeMs: number;
}

interface PlaybackHealthSnapshot {
    incomingByteRate: number;
    bufferDurationMsEma: number;
    keyframeSkipsInWindow: number;
    decoderQueueDepthEma: number;
    currentMaxSpatial: number;
    currentMaxTemporal: number;
    priority: number;
    streamAgeMs: number;
    qualityReductionRequested: boolean;
    /** Smoothed end-to-end latency, ms: server-clock now − frame's effective
     *  capture wall-clock at the moment the frame entered the playback
     *  pipeline. Source for app.video.latency. */
    latencyMsEma: number;
}

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPlayer');

// Graduated recovery thresholds for the latency-tick path. Used by
// reportLatencyTick to escalate response to growing render-frame age:
//   < CATCHUP_GENTLE_MS:   normal
//   ≥ CATCHUP_GENTLE_MS:   shrink pipelineLatencyMs to advance audio target
//   ≥ DROP_TO_KEYFRAME_MS: also clear the decoded slot (forces a fresh pick
//                          on the next render tick)
//   ≥ VIDEO.skipToLiveThresholdMs: SKIP_TO_LIVE
// (SKIP_TO_LIVE / LATENCY_REPORT / SLOW_DECODE thresholds read from VIDEO.*)
const CATCHUP_GENTLE_MS = 300;
const DROP_TO_KEYFRAME_MS = 2000;

// Fixed presentation-side jitter margin. Encoded pre-decode buffer in the
// decoder worker absorbs the bulk of network jitter; this is just a small
// margin that lets a fast-arriving frame replace a pending one before the
// render tick consumes it.
const JITTER_BUFFER_MS = 40;
const DEFAULT_MAX_SPATIAL_LAYER = 2;
const MAX_TEMPORAL_LAYER = 2147483647;
const PLAYBACK_PRIORITY_SECONDARY = 0;
const PLAYBACK_PRIORITY_PRIMARY = 1;

// Late-join catchup: when the rendered frame is much older than the newest
// arrived frame (receiver joined mid-stream, or the sender went through a
// static gap), jump playback forward to the latest buffered frame instead of
// waiting for the ~1x consume rate to catch up. Threshold chosen above typical
// jitter + one heartbeat interval (1s) so we don't thrash on normal playback.
const LATE_JOIN_GAP_MS = 1500;

// Decode performance thresholds — if exceeded on consecutive ticks, trigger quality reduction / codec exclusion
const QUALITY_REDUCTION_TICK_COUNT = 5;    // ~10s of sustained bad performance → request quality reduction
const CODEC_EXCLUSION_TICK_COUNT = 30;     // ~60s after quality reduction still bad → exclude codec
// SLOW_DECODE warmup: skip the first window of samples after decoder init / codec
// switch / tab-restore. Cold-start times for codec init + first keyframe routinely
// exceed the per-frame budget (200–600 ms) before steady-state hits the sub-ms median;
// counting those samples against codec health triggers spurious exclusions.
const SLOW_DECODE_WARMUP_MS = 5000;
const OUTPUT_VERIFICATION_CHECK_INTERVAL_MS = 250;
const OUTPUT_DIMENSION_MISMATCH_TOLERANCE_PX = 16;

interface PendingFrame {
    drawable: VideoFrame | ImageBitmap;
    timestamp: number;
    receivedAtMs: number;
    displayWidth: number;
    displayHeight: number;
    close(): void;
}

const ownedArrayBufferTracker = new OwnedArrayBufferTracker();
const OWNED_ARRAY_BUFFER_LOG_INTERVAL = 300;
function getOwnedArrayBuffer(view: Uint8Array): ArrayBuffer {
    const result = ownedArrayBufferTracker.get(view);
    const stats = ownedArrayBufferTracker.stats;
    if (stats.totalCount % OWNED_ARRAY_BUFFER_LOG_INTERVAL === 0)
        infoLog?.log(`ownedArrayBuffer: fast=${stats.fastCount} ` +
            `slow=${stats.slowCount} (${(stats.fastRatio * 100).toFixed(1)}% fast)`);
    return result;
}

function arrayBufferEqual(a: AllowSharedBufferSource, b: AllowSharedBufferSource): boolean {
    const viewA = ArrayBuffer.isView(a) ? new Uint8Array(a.buffer, a.byteOffset, a.byteLength) : new Uint8Array(a);
    const viewB = ArrayBuffer.isView(b) ? new Uint8Array(b.buffer, b.byteOffset, b.byteLength) : new Uint8Array(b);
    if (viewA.length !== viewB.length) return false;
    for (let i = 0; i < viewA.length; i++) {
        if (viewA[i] !== viewB[i]) return false;
    }
    return true;
}

export class VideoPlayer {
    private blazorRef: DotNet.DotNetObject;
    private streamId: string;
    private authorId: string;
    private canvas: HTMLCanvasElement;
    private videoEl: HTMLVideoElement;
    private bgCanvasEl: HTMLCanvasElement;
    private bgOffscreenTransferred = false;
    private renderBackend: RenderBackend;
    // Stream-creation-time dims from VideoFormat metadata. Used as a
    // canvas-size hint and aspect-ratio target for the container — NOT
    // a verification reference (resolution adapts mid-stream; the
    // sender's per-keyframe dims are the source of truth, captured into
    // lastKeyframeWidth / lastKeyframeHeight).
    private readonly expectedDisplayWidth: number;
    private readonly expectedDisplayHeight: number;
    // Decoder worker (off-main-thread decoding)
    private decoderWorkerInstance: Worker | null = null;
    private decoderWorker: (DecoderWorker & Disposable) | null = null;
    private decoderConfig: DecoderConfig | null = null;
    // Resolves once initDecoderWorker has either finished setting up the worker
    // (decoderWorker assigned + initializeWithStreams/initialize awaited) or
    // bailed out. startPull awaits this so frames cannot arrive before the
    // worker is ready — otherwise pushFrame drops them at `!this.decoderWorker`
    // and the pre-init keyframe is lost, stalling startup until next IDR.
    private decoderReady: Promise<void> = Promise.resolve();
    // Single decoded-frame slot — the doc's `video presentation` replaceable
    // slot. Encoded pre-decode buffer in decoder-worker.ts owns playback
    // latency, so this path only needs the most-recent decoded frame.
    private pendingFrames = new ReplaceableSlot<PendingFrame>({
        dispose: frame => {
            try { frame.close(); } catch { /* already closed */ }
        },
    });
    private readonly isSafari: boolean;
    private conversionQueue: Promise<void> = Promise.resolve();
    private isPlaying = false;
    // Read `isPlaying` through this getter inside async loops to prevent TS
    // control-flow analysis from narrowing it to `true` after an early-return
    // guard — the value can flip to `false` via stop()/dispose() between awaits.
    private get _isPlayingNow(): boolean { return this.isPlaying; }
    private visibilitySubscription: Subscription | null = null;

    // Buffer chunks until we receive a keyframe with description
    private waitingForKeyframe = true;
    private lastDescription: ArrayBuffer | null = null;

    // Decoded-slot health flag (true ⇒ pendingFrames empty). Surfaced to the
    // server via reportPlaying so it can detect render starvation. Real
    // network jitter signal lives on the encoded pre-decode buffer in the
    // decoder worker.
    private lastReportedBufferLow = true;

    // Video pull — Fusion RPC with abort controller for cancellation
    private pullAbortController: AbortController | null = null;
    private pullRetryCount = 0;
    private pullRetryTimer: ReturnType<typeof setTimeout> | null = null;

    // Off-thread mode: when true, the decoder worker owns the Fusion RPC pull
    // and main does no per-frame work. Set after a successful startPullInWorker.
    private offThreadPullActive = false;
    // DIAG: counts entries to delegatePullToWorker for this VideoPlayer instance.
    // Used to confirm whether retry paths re-enter the off-thread setup.
    private delegateEntryCount = 0;
    private connectivityHandlerOnline: EventHandler<boolean> | null = null;
    private connectivityHandlerConnected: EventHandler<boolean> | null = null;
    private sharedSettingsRegistration: Disposable | null = null;

    // Frame pacing state
    private playbackStartTime = 0;     // wall-clock ms (performance.now) when first frame rendered
    private firstFrameTimestamp = 0;    // timestamp of first decoded frame (microseconds)
    private renderRafId = 0;
    private isRenderLoopWaiting = false; // true when RAF is parked because pendingFrames is empty
    private renderFrameCount = 0;       // count of rendered frames (for periodic logging)
    private receivedFrameCount = 0;     // count of received frames (for periodic logging)
    private receivedKeyframeCount = 0;   // count of received keyframes (for correlation with encoder)
    private receivedBytes = 0;           // total bytes received (for bitrate calculation)
    private firstFrameReceivedTime = 0;  // performance.now() when first frame arrived
    private lastSyncLogTime = 0;        // throttle sync logging
    private sequenceNumber = 0;         // sequence number for chunks sent to decoder worker
    private forwardedSpatialLayerId = -1;
    private forwardedWidth = 0;
    private forwardedHeight = 0;
    private observedMaxSpatialLayer = -1;

    // PLI: receiver-requested keyframe
    private lastKeyFrameRequestTime = 0;
    private readonly keyFrameRequestCooldownMs = 10000; // Max 1 request per 10 seconds

    // Render-quality hint state. The latency tick fires every 2 s but
    // is gated on `lastRenderedOffsetMs > 0` — i.e. waits for the first decoded
    // frame. Until then the server has no render-hint cap on this peer and joins
    // it at the top spatial layer; once the canvas has laid out we want to push
    // the hint right away so the cap kicks in within ms, not seconds.
    private resizeObserver: ResizeObserver | null = null;
    private lastSentRenderQuality: number | null | undefined = undefined;

    // Diagnostics counters for 10s delta reporting
    private lastDiagDecodedFrames = 0;
    private lastDiagReceivedFrames = 0;

    // Latency measurement
    private lastRenderedOffsetMs = 0;   // offset of the latest decoded frame (ms from stream start)
    private lastLatencyReportTime = 0;
    // Smoothed video pipeline latency estimate (ms). Cached value of
    // `pipelineLatencyEma.value` — refreshed wherever the EMA is mutated.
    // Source for app.video.latency, audio-sync target derivation, and
    // diagnostics. Smoothing factor 0.2 ≈ 90% convergence in ~10 samples.
    private pipelineLatencyMs = 0;
    private readonly pipelineLatencyEma = new RunningEMA(0, 1, 0.2);
    // EMAs over the per-tick playback-health samples. 10-sample window
    // (α = 2/(N+1) ≈ 0.182); reset alongside pipelineLatencyEma on stream
    // restart so the verdict isn't biased by the previous session's tail.
    private readonly bufferDurationMsEma = new RunningEMA(0, 10);
    private readonly decoderQueueDepthEma = new RunningEMA(0, 10);
    private lastSkipToLiveTime = 0;     // Cooldown: prevent rapid SKIP_TO_LIVE cascading
    private skipFramesBelowOffsetMs = 0; // Live gate: skip decoded frames below this offset
    private skippedBacklogFrames = 0;
    private rebufferDelayMs = 0;         // After tab restore, delay rendering to let buffer accumulate
    private consecutiveEmptyRenders = 0; // Safety net: count consecutive RAFs with no frame rendered
    private pendingNotDueSinceMs = 0;     // First time the one-slot decoded queue became blocked by presentation timing
    private lastHighLatencyLogTime = 0;  // Throttle high-latency FRAME_RECV logs
    private skipToLiveCount = 0;          // Number of skip-to-live events
    private lastQualitySkipToLiveCount = 0;
    private readonly createdAtMs = performance.now();
    // Offset of the newest frame that arrived at this receiver. Used for server
    // latency reporting so the signal reflects pure network+relay transit —
    // NOT pipelineLatencyMs (the intentional jitter buffer). Reporting
    // lastRenderedOffsetMs would conflate the buffer with congestion and make
    // the server step down quality on a perfectly healthy local link.
    private lastArrivedOffsetMs = 0;

    // (Adaptive jitter / inter-frame measurement removed — encoded pre-decode
    // buffer in the decoder worker is the real jitter absorber. Presentation
    // uses the fixed JITTER_BUFFER_MS margin.)

    // RTT measurement for proactive congestion detection
    private smoothedRttMs = 0;
    private previousRttMs = 0;
    private rttGradientMs = 0;

    // Late-join catchup cooldown (wall-clock fallback path).
    private lastSeekTime = 0;
    private readonly seekCooldownMs = 5000;

    // Decode performance tracking (Phase 1 & 2: quality reduction / codec exclusion)
    private codecSlowTickCount = 0;            // consecutive bad decode ticks (each tick = 2s)
    private qualityReductionRequested = false;  // true after Phase 1 quality reduction was requested
    private codecCategory = '';         // 'av1', 'hevc', 'vp9', 'h264' — derived from codec string
    private decoderWarmupUntilMs = 0;          // performance.now() before this → skip SLOW_DECODE detector
    private outputVerificationTimer: ReturnType<typeof globalThis.setInterval> | undefined;
    private outputVerified = false;
    private outputVerificationFailed = false;
    private codecExclusionRequested = false;
    // Latest keyframe's transmitted dims (VideoFrameDto.Width / Height).
    // The reference for output verification — see
    // feedback_video_dim_verification_per_frame.md. Stream-metadata
    // dims (expectedDisplayWidth / Height) are NOT a verification
    // reference because resolution adapts mid-stream.
    private lastKeyframeWidth = 0;
    private lastKeyframeHeight = 0;

    // Audio sync
    private startedAtMs: number;

    // Stream mode state
    private readonly useStreams: boolean;
    private chunkInputChannel: StreamEndpoints<RawChunkMessage> | null = null;

    /** Creates a new VideoPlayer instance for Blazor interop */
    static create(
        canvas: HTMLCanvasElement,
        videoEl: HTMLVideoElement,
        bgCanvasEl: HTMLCanvasElement,
        blazorRef: DotNet.DotNetObject,
        streamId: string,
        authorId: string,
        codec: string,
        width: number,
        height: number,
        codecSettings: string,
        startedAtMs: number
    ): VideoPlayer {
        return new VideoPlayer(blazorRef, streamId, authorId, codec, width, height, codecSettings, canvas, videoEl, bgCanvasEl, startedAtMs);
    }

    constructor(
        blazorRef: DotNet.DotNetObject,
        streamId: string,
        authorId: string,
        codec: string,
        width: number,
        height: number,
        codecSettings: string,
        canvas: HTMLCanvasElement,
        videoEl: HTMLVideoElement,
        bgCanvasEl: HTMLCanvasElement,
        startedAtMs: number
    ) {
        this.blazorRef = blazorRef;
        this.streamId = streamId;
        this.authorId = authorId;
        this.startedAtMs = startedAtMs;
        this.canvas = canvas;
        this.videoEl = videoEl;
        this.bgCanvasEl = bgCanvasEl;
        this.expectedDisplayWidth = width || 1280;
        this.expectedDisplayHeight = height || 720;
        this.renderBackend = pickRenderBackend(canvas, videoEl);
        // Hide the inactive surface via inline style on the element itself.
        // Inline `style.display` survives Blazor re-renders of the parent's
        // class attribute (Razor template binds parent.class via @FocusedClass,
        // not these elements' style). Previously we toggled a `.backend-mstg`
        // class on the parent and gated visibility via CSS — Blazor's class
        // diff stripped it on layout flips, producing the "black sidebar tile
        // / blur-only focused" symptom.
        this.applyBackendVisibility(canvas, videoEl);
        const container = canvas.parentElement;
        if (container) {
            container.classList.add('output-unverified');
        }
        this.isSafari = /^((?!chrome|android).)*safari/i.test(navigator.userAgent);
        this.useStreams = supportsTransferableStreams();
        if (this.isSafari)
            infoLog?.log('Safari detected — will convert VideoFrame to ImageBitmap for canvas rendering');

        // Set canvas size
        canvas.width = width || 1280;
        canvas.height = height || 720;

        debugLog?.log(
            `VideoPlayer created for stream ${streamId}, codec: ${codec}, size: ${width}x${height}, ` +
            `authorId=${authorId}, startedAtMs=${startedAtMs.toFixed(0)}`);

        // Register in global diagnostics registry
        activePlayers.set(streamId, this);
        infoLog?.log(`VideoPlayer registry: added ${streamId}, active=${activePlayers.size}`);

        // Initialize decoder worker — store the promise so startPull can gate
        // on it (prevents pre-init frame drop on the main-thread RPC fallback).
        this.decoderReady = this.initDecoderWorker(codec, width, height, codecSettings);
    }

    // Hide the inactive render surface via inline `style.display`. Razor doesn't
    // bind `style` on these elements, so Blazor's diff never overwrites it —
    // unlike a parent class toggle, which gets clobbered when `FocusedClass`
    // changes during a layout flip. Called once at construction and again on
    // the mstg → canvas fallback in startPull().
    private applyBackendVisibility(canvas: HTMLCanvasElement, videoEl: HTMLVideoElement): void {
        if (this.renderBackend.kind === 'mstg') {
            videoEl.style.display = 'block';
            canvas.style.display = 'none';
        } else {
            canvas.style.display = 'block';
            videoEl.style.display = 'none';
        }
    }

    private async initDecoderWorker(codec: string, width: number, height: number, codecSettings: string): Promise<void> {
        if (!this.supportsWebCodecs()) {
            warnLog?.log('WebCodecs not supported');
            return;
        }

        try {
            // Decode codec settings (base64 encoded SPS/PPS for H.264)
            let description: ArrayBuffer | undefined;
            if (codecSettings) {
                const binaryString = atob(codecSettings);
                const bytes = new Uint8Array(binaryString.length);
                for (let i = 0; i < binaryString.length; i++) {
                    bytes[i] = binaryString.charCodeAt(i);
                }
                description = bytes.buffer;
                debugLog?.log(`Decoded description: ${bytes.length} bytes`);
            }

            // Build ordered list of candidate codec strings to try
            const candidates = getCodecCandidates(codec, description);
            debugLog?.log(`Codec candidates: [${candidates.join(', ')}]`);

            // Probe with full config (codec + description + dimensions). HW only —
            // no `'no-preference'` fallback (that lets the browser silently land
            // on SW). If no HW-supported candidate matches the description,
            // surface a stream-level failure.
            const dims = (width && height) ? { width, height } : undefined;
            const selection = await selectDecoderCodec(candidates, description, dims);
            if (!selection) {
                warnLog?.log(`No HW-supported codec found among candidates: [${candidates.join(', ')}]`);
                this.isPlaying = false;
                void this.reportEnded(`Codec not supported`);
                return;
            }
            const codecString = selection.codec;
            const bestAcceleration = selection.hardwareAcceleration;
            debugLog?.log(`Selected decoder codec: ${codecString} (accel: ${bestAcceleration})`);
            debugLog?.log(`Initializing decoder worker with codec: ${codecString}`);

            // Derive codec category for performance tracking
            this.codecCategory = VideoPlayer.getCodecCategory(codecString);
            this.codecSlowTickCount = 0;
            this.qualityReductionRequested = false;
            this.decoderWarmupUntilMs = performance.now() + SLOW_DECODE_WARMUP_MS;

            this.decoderConfig = {
                codec: codecString,
                optimizeForLatency: true,
                hardwareAcceleration: bestAcceleration,
                description,
                codedWidth: width || undefined,
                codedHeight: height || undefined,
            };
            this.startOutputVerificationMonitor();

            // Create decoder worker
            const decoderWorkerPath = Versioning.mapPath('/dist/videoDecoderWorker.js');
            this.decoderWorkerInstance = new Worker(decoderWorkerPath, { type: 'module' });
            this.decoderWorkerInstance.onerror = (e) => errorLog?.log('Decoder worker error:', e);

            // Create RPC proxy (used for control messages in both modes + data path in fallback)
            this.decoderWorker = rpcClientServer<DecoderWorker>(
                'VideoPlayer.decoder',
                this.decoderWorkerInstance,
                {
                    getSessionToken: (minLifespanMs?: number) => Api.getSessionToken(minLifespanMs),
                    onDecodedFrame: (frame: VideoFrame) => { this.onFrameDecoded(frame); return Promise.resolve(); },
                    onOffThreadTrackReady: (track: MediaStreamTrack) => {
                        const backend = this.renderBackend as { onTrackReady?: (t: MediaStreamTrack) => void };
                        if (typeof backend.onTrackReady === 'function')
                            backend.onTrackReady(track);
                        else
                            try { track.stop(); } catch { /* ignore */ }
                        return Promise.resolve();
                    },
                    onLatencyReport: (report: DecoderWorkerLatencyReport) => {
                        this.onWorkerLatencyReport(report);
                        return Promise.resolve();
                    },
                    onPullEnded: (errorMessage: string | null) => {
                        void this.reportEnded(errorMessage ?? undefined);
                        return Promise.resolve();
                    },
                }
            );

            // Propagate app constants. Fire-and-forget: the message queues
            // ahead of `initialize` / `prewarmRpc` / any other RPC.
            await whenAppConstantsReady;
            void this.decoderWorker.init(AC, SharedSettings.all);
            this.sharedSettingsRegistration = SharedSettingsWorkerSync.register(this.decoderWorker);

            // Mirror main-thread ConnectivityUI → worker's WorkerConnectivityUI
            // so the worker's Api peer honours `isDotNetRpcConnected`.
            const pushConnectivity = (): void => {
                if (!this.decoderWorker) return;
                void this.decoderWorker.onConnectivityUpdate(
                    ConnectivityUI.isOnline,
                    ConnectivityUI.isConnected,
                    ConnectivityUI.isBlazorServer,
                    rpcNoWait);
            };
            this.connectivityHandlerOnline = ConnectivityUI.isOnlineChanged.add(pushConnectivity);
            this.connectivityHandlerConnected = ConnectivityUI.isConnectedChanged.add(pushConnectivity);
            void ConnectivityUI.whenReady.then(pushConnectivity);

            // Wire focused-state changes to the worker's blur-paint gate.
            // Sidebar / unfocused tiles hide the bg canvas via CSS, so painting
            // it is wasted CPU. The mstg backend already observes parent class
            // for play() retries — piggyback on the same observer.
            if (this.renderBackend.kind === 'mstg') {
                const mstgBackend = this.renderBackend as OffThreadRenderBackend;
                mstgBackend.onFocusedChange = (focused: boolean) => {
                    if (!this.decoderWorker) return;
                    void this.decoderWorker.setBgPaintEnabled(focused, rpcNoWait);
                };
            }

            if (this.useStreams) {
                // Stream mode: transfer input stream to worker, output via RPC callback
                this.chunkInputChannel = createInputChannel<RawChunkMessage>(4);

                await this.decoderWorker.initializeWithStreams(
                    this.decoderConfig,
                    this.chunkInputChannel.readable,
                    { type: 'rpc-timeout', timeoutMs: 5000 },
                );
                // Decoded frames arrive via onDecodedFrame RPC callback (postMessage+transfer)
                debugLog?.log('Decoder worker initialized (stream input, RPC output)');
            } else {
                // RPC fallback
                await this.decoderWorker.initialize(this.decoderConfig, { type: 'rpc-timeout', timeoutMs: 5000 });
                debugLog?.log('Decoder worker initialized (RPC mode)');
            }

            // Pre-warm the worker's Fusion RPC WebSocket in parallel with the
            // rest of init. Without this, the WS handshake blocks the first
            // chunk delivery inside startPullInWorker — adds visible delay to
            // the rotating-indicator window on iOS.
            const apiUrl = BrowserInit.getUrl('/rpc/ws').replace(/^http/, 'ws');
            SharedSettings.update({ apiUrl });
            void this.decoderWorker.prewarmRpc(apiUrl, rpcNoWait);

            // Off-thread renderer activation now happens inside `startPull`,
            // because the worker needs streamId + skipToMs to start the pull
            // loop in one shot.

            // If we have codec settings (SPS/PPS for H.264/HEVC) we don't need
            // to wait for a keyframe with description — the description alone
            // configures the decoder. Other codecs (incl. AV1) still wait for
            // the first keyframe; VideoStreamFilter.Apply guarantees the first
            // delivered frame after skipTo is a keyframe.
            if (codecSettings) {
                this.waitingForKeyframe = false;
                debugLog?.log(`Not waiting for keyframe with description (codecSettings=true)`);
            }
        } catch (error) {
            errorLog?.log('Failed to initialize decoder worker:', error);
        }
    }

    private supportsWebCodecs(): boolean {
        return typeof VideoDecoder !== 'undefined';
    }

    private static getCodecCategory(codecString: string): string {
        const lc = codecString.toLowerCase();
        if (lc.startsWith('hev1') || lc.startsWith('hvc1')) return 'hevc';
        if (lc.startsWith('av01')) return 'av1';
        if (lc.startsWith('vp09') || lc.startsWith('vp9')) return 'vp9';
        if (lc.startsWith('avc1') || lc.startsWith('h264')) return 'h264';
        return 'unknown';
    }

    private wrapFrame(frame: VideoFrame, receivedAtMs: number): PendingFrame {
        return {
            drawable: frame,
            timestamp: frame.timestamp,
            receivedAtMs,
            displayWidth: frame.displayWidth,
            displayHeight: frame.displayHeight,
            close() { frame.close(); },
        };
    }

    private async convertToBitmap(frame: VideoFrame, receivedAtMs: number): Promise<PendingFrame> {
        const ts = frame.timestamp;
        const dw = frame.displayWidth;
        const dh = frame.displayHeight;
        try {
            const bitmap = await createImageBitmap(frame);
            frame.close();
            return {
                drawable: bitmap,
                timestamp: ts,
                receivedAtMs,
                displayWidth: dw,
                displayHeight: dh,
                close() { bitmap.close(); },
            };
        } catch (e) {
            warnLog?.log('createImageBitmap(VideoFrame) failed, falling back to direct frame:', e);
            return {
                drawable: frame,
                timestamp: ts,
                receivedAtMs,
                displayWidth: dw,
                displayHeight: dh,
                close() { frame.close(); },
            };
        }
    }

    private enqueuePendingFrame(pf: PendingFrame): void {
        // Single-slot replace (push closes any prior frame). Multi-frame
        // soft-catchup / hard-cap dropped — encoded pre-decode buffer in
        // decoder-worker.ts owns playback latency now.
        this.pendingFrames.push(pf);
        this.wakeRenderLoop();

        // Update pipeline latency estimate from this fresh frame
        const frameOffsetMs = pf.timestamp / 1000; // μs → ms
        const capturedAtMs = this.startedAtMs + frameOffsetMs;
        const currentLatencyMs = ServerClock.now() - capturedAtMs;
        // Safety cap at 10s to prevent absurd values from clock drift.
        const cappedLatencyMs = Math.min(Math.max(currentLatencyMs, 0), 10000);
        this.pipelineLatencyEma.appendSample(cappedLatencyMs);
        this.pipelineLatencyMs = this.pipelineLatencyEma.value;
    }

    private onFrameDecoded(frame: VideoFrame): void {
        const receivedAtMs = performance.now();
        // Live gate: skip old frames from decoder's internal backlog.
        if (this.skipFramesBelowOffsetMs > 0) {
            const frameOffsetMs = frame.timestamp / 1000; // μs → ms
            if (frameOffsetMs < this.skipFramesBelowOffsetMs) {
                frame.close();
                this.skippedBacklogFrames++;
                if (this.skippedBacklogFrames <= 3 || this.skippedBacklogFrames % 10 === 0) {
                    debugLog?.log(
                        `Skipping backlog frame #${this.skippedBacklogFrames}: ` +
                        `frameOffset=${frameOffsetMs.toFixed(0)}ms, threshold=${this.skipFramesBelowOffsetMs.toFixed(0)}ms`);
                }
                return;
            }
            // Caught up — resume normal rendering
            debugLog?.log(
                `Decoder backlog cleared: skipped ${this.skippedBacklogFrames} frames, ` +
                `resumed at offset ${frameOffsetMs.toFixed(0)}ms (threshold was ${this.skipFramesBelowOffsetMs.toFixed(0)}ms)`);
            this.skipFramesBelowOffsetMs = 0;
        }

        // Safari needs VideoFrame → ImageBitmap conversion to make canvas2D
        // drawImage cheap. The MSTG backend wants the original VideoFrame
        // (a `<video>` element accepts native frames straight from the decoder).
        if (this.isSafari && this.renderBackend.kind === 'canvas') {
            this.conversionQueue = this.conversionQueue.then(async () => {
                if (!this.isPlaying) { frame.close(); return; }
                const pf = await this.convertToBitmap(frame, receivedAtMs);
                // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
                if (!this.isPlaying) { pf.close(); return; }
                this.enqueuePendingFrame(pf);
            });
        } else {
            this.enqueuePendingFrame(this.wrapFrame(frame, receivedAtMs));
        }
    }

    private onDecoderError(error: Error): void {
        errorLog?.log('Decoder error:', error);
        void this.reportEnded(error.message);
    }

    private renderTick = (): void => {
        this.renderRafId = 0;
        if (!this.isPlaying)
            return;

        this.onRenderFrame();

        // RAF gating: park the loop when nothing is buffered. enqueuePendingFrame
        // wakes it via wakeRenderLoop() on the next arrival. Avoids 60Hz wakeups
        // (audio-sync reads, timestamp math, sync logging) during stalls.
        if (this.pendingFrames.length === 0) {
            this.isRenderLoopWaiting = true;
            return;
        }

        this.renderRafId = requestAnimationFrame(this.renderTick);
    };

    private startRenderLoop(): void {
        if (this.renderRafId !== 0)
            return;
        this.isRenderLoopWaiting = false;
        this.renderRafId = requestAnimationFrame(this.renderTick);
    }

    private wakeRenderLoop(): void {
        if (!this.isRenderLoopWaiting || !this.isPlaying || this.renderRafId !== 0)
            return;
        this.isRenderLoopWaiting = false;
        this.renderRafId = requestAnimationFrame(this.renderTick);
    }

    private stopRenderLoop(): void {
        if (this.renderRafId !== 0) {
            cancelAnimationFrame(this.renderRafId);
            this.renderRafId = 0;
        }
        this.isRenderLoopWaiting = false;
    }

    private onRenderFrame(): void {
        if (!this.isPlaying || this.pendingFrames.length === 0) return;

        const now = performance.now();

        // Initialize timing anchor on first frame
        if (this.playbackStartTime === 0) {
            this.playbackStartTime = now + this.rebufferDelayMs;
            this.rebufferDelayMs = 0;
            // Anchor to near real-time: skip ahead to where live frames should be,
            // rather than pacing stale buffered frames at 1x from their old timestamps.
            // This makes the renderer immediately drop stale frames and start from the latest.
            const liveOffsetMs = ServerClock.now() - this.startedAtMs;
            this.firstFrameTimestamp = liveOffsetMs * 1000; // ms → μs
        }

        this.renderFrameCount++;

        // Wall-clock pacing. Late-join catchup uses the gap between newest
        // arrived and newest rendered offsets — works on sparse-heartbeat
        // streams (e.g. static screencast) where the single-slot decoded
        // queue never accumulates even when we're behind live. With encoded
        // buffer pacing upstream there is no bufferSpan-based hard-seek or
        // playbackRate chase to perform.
        const liveGapMs = this.lastArrivedOffsetMs - this.lastRenderedOffsetMs;
        if (liveGapMs > LATE_JOIN_GAP_MS
            && this.pendingFrames.length > 0
            && (now - this.lastSeekTime) > this.seekCooldownMs) {
            const latestTimestamp = this.pendingFrames.peekBack()!.timestamp;
            this.playbackStartTime = now;
            this.firstFrameTimestamp = latestTimestamp;
            this.lastSeekTime = now;
            warnLog?.log(
                `Late-join catchup: jumped to live edge, ` +
                `lastArrivedMs=${this.lastArrivedOffsetMs.toFixed(0)}, ` +
                `lastRenderedMs=${this.lastRenderedOffsetMs.toFixed(0)}, ` +
                `gapMs=${liveGapMs.toFixed(0)}`);
        }

        const elapsedUs = (now - this.playbackStartTime) * 1000;
        const targetTimestamp = this.firstFrameTimestamp + elapsedUs;

        if (now - this.lastSyncLogTime > 2000) {
            this.lastSyncLogTime = now;
            debugLog?.log(`wallClock: authorId=${this.authorId}, pending=${this.pendingFrames.length}`);
        }

        if (this.renderFrameCount % 60 === 0) {
            debugLog?.log(
                `onRenderFrame #${this.renderFrameCount}: lastRenderedOffsetMs=${this.lastRenderedOffsetMs.toFixed(0)}, ` +
                `pendingFrames=${this.pendingFrames.length}`);
        }

        // Apply small jitter buffer: render decoded frames slightly behind
        // target so fast-arriving frames have a chance to replace pending
        // ones before presentation. Encoded buffer absorbs the bulk of
        // jitter upstream — keep this as a small fixed margin (40 ms).
        const adjustedTargetTimestamp = targetTimestamp - JITTER_BUFFER_MS * 1000;

        // Single-slot pick: render pending if it's at or behind the target.
        let frameToRender: PendingFrame | null = null;
        const front = this.pendingFrames.peekFront();
        if (front && front.timestamp <= adjustedTargetTimestamp) {
            frameToRender = this.pendingFrames.shift()!;
        }

        if (frameToRender) {
            if (this.presentFrame(frameToRender, 'render-frame'))
                return;
        } else if (front) {
            if (this.pendingNotDueSinceMs === 0)
                this.pendingNotDueSinceMs = front.receivedAtMs;
            const deadlineMs = this.pendingNotDueSinceMs + VIDEO.targetBufferDurationMs + VIDEO.frameDurationMs;
            if (now >= deadlineMs) {
                const earlyByMs = Math.max(0, (front.timestamp - adjustedTargetTimestamp) / 1000);
                const waitMs = now - this.pendingNotDueSinceMs;
                warnLog?.log(
                    `Render deadline reached after ${waitMs.toFixed(0)}ms, ` +
                    `presenting pending frame early by ${earlyByMs.toFixed(0)}ms`);
                // Single decoded slot: if we are stuck on the only available
                // frame, the wall-clock anchor is wrong for this stream segment.
                // Present it now and re-anchor so the next arrival is paced from
                // the frame we actually showed rather than from a stale live-edge
                // estimate.
                this.playbackStartTime = now - JITTER_BUFFER_MS;
                this.firstFrameTimestamp = front.timestamp;
                frameToRender = this.pendingFrames.shift()!;
                if (this.presentFrame(frameToRender, 'render-stuck'))
                    return;
            }
            else
                this.consecutiveEmptyRenders++;
        } else {
            this.consecutiveEmptyRenders = 0;
            this.pendingNotDueSinceMs = 0;
        }

        this.updateBufferState();

        // Report latency from RAF — naturally pauses when tab is hidden,
        // preventing stale reports that trigger server-side skip-to-live
        if (now - this.lastLatencyReportTime >= VIDEO.latencyReportIntervalMs) {
            this.lastLatencyReportTime = now;
            this.reportLatencyTick();
        }
    }

    private updateBufferState(): void {
        // Decoded slot is single-frame; "low" means nothing pending. Real
        // jitter signal lives on the encoded pre-decode buffer in the
        // decoder worker.
        const isBufferLow = this.pendingFrames.isEmpty();
        if (isBufferLow !== this.lastReportedBufferLow) {
            this.lastReportedBufferLow = isBufferLow;
            void this.reportPlaying(0, isBufferLow);
        }
    }

    private startOutputVerificationMonitor(): void {
        if (this.outputVerificationTimer !== undefined || this.outputVerified)
            return;
        this.outputVerificationTimer = globalThis.setInterval(
            () => this.checkOutputVerification('timer'),
            OUTPUT_VERIFICATION_CHECK_INTERVAL_MS);
        globalThis.setTimeout(() => this.checkOutputVerification('startup'), 0);
    }

    private stopOutputVerificationMonitor(): void {
        if (this.outputVerificationTimer === undefined)
            return;
        globalThis.clearInterval(this.outputVerificationTimer);
        this.outputVerificationTimer = undefined;
    }

    private presentFrame(frameToRender: PendingFrame, verificationReason: string): boolean {
        this.lastRenderedOffsetMs = frameToRender.timestamp / 1000;
        this.renderBackend.drawFrame(frameToRender);
        const shouldStop = this.checkOutputVerification(verificationReason, {
            width: frameToRender.displayWidth,
            height: frameToRender.displayHeight,
        });
        frameToRender.close();
        this.consecutiveEmptyRenders = 0;
        this.pendingNotDueSinceMs = 0;
        return shouldStop;
    }

    private checkOutputVerification(reason: string, reference?: { width: number; height: number }): boolean {
        if (this.outputVerified || !this.isPlaying)
            return false;

        // Reference dims are either the just-presented decoded frame (canvas
        // path) or the worker-reported decoded output (MSTG path). Fall back to
        // the latest keyframe only for startup/timer probes before a frame-
        // specific reference is available. Stream-metadata dims are just a
        // creation-time snapshot; resolution adapts mid-stream.
        const refW = reference?.width ?? this.lastKeyframeWidth;
        const refH = reference?.height ?? this.lastKeyframeHeight;
        if (refW <= 0 || refH <= 0) {
            // No keyframe with dims yet — wait. Off-thread mode learns
            // these via the worker latency report (~2 s cadence);
            // main-thread RPC mode learns them on every keyframe
            // pushFrame call.
            return false;
        }

        const output = this.renderBackend.getOutputSize();
        if (!output || output.width <= 0 || output.height <= 0)
            return false;

        const widthMismatch = Math.abs(output.width - refW) > OUTPUT_DIMENSION_MISMATCH_TOLERANCE_PX;
        const heightMismatch = Math.abs(output.height - refH) > OUTPUT_DIMENSION_MISMATCH_TOLERANCE_PX;
        if (!widthMismatch && !heightMismatch) {
            this.markOutputVerified(reason, output.width, output.height);
            return false;
        }

        if (!this.outputVerificationFailed) {
            this.outputVerificationFailed = true;
            warnLog?.log(
                `checkOutputVerification: failed, decoded ${output.width}x${output.height} ` +
                `does not match latest keyframe ${refW}x${refH} ` +
                `(${reason}); codec=${this.codecCategory || 'unknown'}`);
        }

        if (this.shouldRequestCodecExclusion() && !this.codecExclusionRequested) {
            this.codecExclusionRequested = true;
            this.stopOutputVerificationMonitor();
            warnLog?.log(`checkOutputVerification: requesting codec exclusion for ${this.codecCategory}`);
            void this.blazorRef.invokeMethodAsync('OnRequestCodecExclusion', this.codecCategory);
            return true;
        }
        return false;
    }

    private markOutputVerified(reason: string, width: number, height: number): void {
        this.outputVerified = true;
        this.outputVerificationFailed = false;
        this.stopOutputVerificationMonitor();
        this.canvas.parentElement?.classList.remove('output-unverified');
        debugLog?.log(`checkOutputVerification: ok, ${width}x${height} (${reason})`);
    }

    private shouldRequestCodecExclusion(): boolean {
        return this.codecCategory !== ''
            && this.codecCategory !== 'h264'
            && this.codecCategory !== 'unknown';
    }

    public pushFrame(
        frameData: Uint8Array,
        timestampMs: number,
        durationMs: number,
        isKeyFrame: boolean,
        description?: Uint8Array,
        width?: number,
        height?: number,
    ): void {
        if (!this.isPlaying || !this.decoderWorker) {
            return;
        }
        // Main-thread RPC mode: capture the keyframe's transmitted dims
        // for output verification. (Off-thread mode reads them from the
        // worker's latency report instead.)
        if (isKeyFrame && width && height) {
            this.lastKeyframeWidth = width;
            this.lastKeyframeHeight = height;
        }

        // Live gate: skip stale encoded frames arriving from the RPC stream.
        if (this.skipFramesBelowOffsetMs > 0 && timestampMs < this.skipFramesBelowOffsetMs) {
            this.waitingForKeyframe = true;
            return;
        }

        // If we're waiting for a keyframe with description, buffer chunks
        if (this.waitingForKeyframe) {
            if (isKeyFrame && frameData.length === 0) {
                debugLog?.log(`Skipping empty-data keyframe at offset ${timestampMs.toFixed(0)}ms, descLen=${description?.length ?? 0}`);
                return;
            }
            const needsDescription = !!this.decoderConfig?.description;
            if (isKeyFrame && (!needsDescription || (description && description.length > 0))) {
                // Live gate: skip keyframes that are too old.
                if (this.skipFramesBelowOffsetMs > 0 && timestampMs < this.skipFramesBelowOffsetMs) {
                    debugLog?.log(`Skipping old keyframe at offset ${timestampMs.toFixed(0)}ms ` +
                        `(threshold=${this.skipFramesBelowOffsetMs.toFixed(0)}ms)`);
                    return;
                }
                this.skipFramesBelowOffsetMs = 0;

                debugLog?.log(`Got keyframe: descLen=${description?.length ?? 0}, needsDesc=${needsDescription}`);
                this.waitingForKeyframe = false;

                // Reconfigure decoder worker with description if needed
                if (description && description.length > 0 && this.decoderConfig) {
                    const descBuffer = description.buffer.slice(
                        description.byteOffset,
                        description.byteOffset + description.byteLength
                    );
                    this.lastDescription = descBuffer as ArrayBuffer;

                    // Re-derive codec from description (defense-in-depth)
                    const derivedCodec = mapCodecToWebCodecs(
                        this.decoderConfig.codec, descBuffer as ArrayBuffer);

                    const newConfig: DecoderConfig = {
                        ...this.decoderConfig,
                        codec: derivedCodec,
                        description: descBuffer,
                    };
                    this.decoderConfig = newConfig;
                    void this.decoderWorker.configureDecoder(newConfig);
                }

                // Send keyframe to decoder worker
                this.sendToDecoderWorker(frameData, timestampMs, durationMs, isKeyFrame, description, width, height);
            }
            // Drop delta frames while waiting for keyframe
            return;
        }

        // If we receive a new keyframe with description, reconfigure only if changed
        if (isKeyFrame && description && description.length > 0) {
            const descBuffer = description.buffer.slice(
                description.byteOffset,
                description.byteOffset + description.byteLength
            );
            const descChanged = !this.lastDescription || !arrayBufferEqual(this.lastDescription, descBuffer);
            if (descChanged) {
                debugLog?.log(`Reconfiguring decoder worker with new description: ${description.length} bytes`);
                this.lastDescription = descBuffer as ArrayBuffer;

                if (this.decoderConfig) {
                    const derivedCodec = mapCodecToWebCodecs(
                        this.decoderConfig.codec, descBuffer as ArrayBuffer);
                    const newConfig: DecoderConfig = {
                        ...this.decoderConfig,
                        codec: derivedCodec,
                        description: descBuffer,
                    };
                    this.decoderConfig = newConfig;
                    void this.decoderWorker.configureDecoder(newConfig);
                }
                this.playbackStartTime = 0;
                this.pipelineLatencyEma.reset();
                this.bufferDurationMsEma.reset();
                this.decoderQueueDepthEma.reset();
                this.pipelineLatencyMs = 0; // stale value causes render stall after reconfigure

                // Flush old pending frames — they're from the old decoder at stale offsets.
                // Keeping them creates a multi-second render stall (offset gap).
                while (!this.pendingFrames.isEmpty()) {
                    try { this.pendingFrames.shift()!.close(); } catch { /* already closed */ }
                }
            }
        }

        this.sendToDecoderWorker(frameData, timestampMs, durationMs, isKeyFrame, description, width, height);
    }

    private sendToDecoderWorker(
        frameData: Uint8Array,
        timestampMs: number,
        durationMs: number,
        isKeyFrame: boolean,
        description?: Uint8Array,
        width?: number,
        height?: number,
    ): void {
        if (!this.decoderWorker) return;

        // Extract owned ArrayBuffers — fast path uses the underlying buffer
        // directly when the Uint8Array view spans the whole buffer (msgpack
        // returns owned buffers for top-level bin fields). On the cross-worker
        // hop the RPC framework auto-detects trailing ArrayBuffer args and
        // transfers them (zero-copy across the postMessage boundary).
        const dataBuffer = getOwnedArrayBuffer(frameData);
        let descBuffer: ArrayBuffer | undefined;
        if (description && description.length > 0) {
            descBuffer = getOwnedArrayBuffer(description);
        }

        if (this.useStreams && this.chunkInputChannel) {
            // Stream mode: write to input stream
            void this.chunkInputChannel.writer.write({
                timestamp: timestampMs * 1000, // ms → μs
                duration: durationMs * 1000,   // ms → μs
                isKeyFrame,
                sequenceNumber: this.sequenceNumber++,
                data: dataBuffer,
                description: descBuffer,
                width,
                height,
            }).catch((e: unknown) => {
                if (!this.isPlaying) return;
                warnLog?.log('Decoder input stream write failed:', e);
                this.armLiveKeyframeGate('decoder-input-write-failed', timestampMs);
            });
        } else {
            // RPC fallback: send raw bytes to worker
            void this.decoderWorker.decodeRawChunk(
                timestampMs * 1000, // ms → μs
                durationMs * 1000,  // ms → μs
                isKeyFrame,
                this.sequenceNumber++,
                width,
                height,
                dataBuffer,
                descBuffer,
                rpcNoWait
            ).catch((e: unknown) => {
                if (!this.isPlaying) return;
                warnLog?.log('Decoder input RPC failed:', e);
                this.armLiveKeyframeGate('decoder-input-rpc-failed', timestampMs);
            });
        }
    }


    public start(): void {
        if (this.isPlaying) return;

        this.isPlaying = true;
        this.startOutputVerificationMonitor();
        // Off-thread MSTG path: the worker drives selection + writes; main
        // thread has no per-frame work. Skip the RAF loop entirely.
        if (!this.renderBackend.isOffThread)
            this.startRenderLoop();
        // Per-instance scope — refcounts across concurrent players so one
        // stopping doesn't park the peer that other players still need.
        Api.requireConnection(`VideoPlayer:${this.streamId}`);
        debugLog?.log(`VideoPlayer started for stream ${this.streamId}`);

        // Listen for tab visibility restore to avoid frame burst after backgrounding
        this.visibilitySubscription = DocumentEvents.passive.visibilityChange$.subscribe(() => {
            if (!document.hidden && this.isPlaying) {
                debugLog?.log('visibilityChange: tab became visible');
                this.onVisibilityRestored();
            }
        });

        // Watch the canvas for layout changes and send a render-hint-only
        // ReportVideoLatency whenever the implied quality level flips between
        // buckets (Low/Medium/High/Full/Ultra). The latency tick won't run
        // until first frame is rendered, so without this the server treats this
        // peer as uncapped for several seconds — bandwidth waste on multi-tile
        // layouts where the canvas is much smaller than the source resolution.
        this.resizeObserver = new ResizeObserver(() => this.maybeSendRenderHint());
        this.resizeObserver.observe(this.canvas);
        // Initial fire — ResizeObserver delivers the first entry asynchronously,
        // but we want the hint to land before the first ReportVideoLatency tick.
        this.maybeSendRenderHint();

        // Report initial playing state
        void this.reportPlaying(0, true);
    }

    private maybeSendRenderHint(): number | null | undefined {
        const level = this.computeRenderQualityLevel();
        if (level === this.lastSentRenderQuality) return undefined;
        this.lastSentRenderQuality = level;
        const currentMaxSpatial = maxSpatialForRenderQualityLevel(level);
        const priority = priorityForRenderQualityLevel(level);
        void this.blazorRef.invokeMethodAsync('OnPlaybackRenderHint', currentMaxSpatial, priority)
            .catch((e: unknown) => warnLog?.log('OnPlaybackRenderHint error:', e));
        debugLog?.log(
            `RenderQuality hint: level=${level ?? 'uncapped'} maxSpatial=${currentMaxSpatial} ` +
            `priority=${priority} (canvas=${this.canvas.clientWidth}x${this.canvas.clientHeight})`);
        return level;
    }

    private requestKeyFrame(): void {
        const now = performance.now();
        if (now - this.lastKeyFrameRequestTime < this.keyFrameRequestCooldownMs)
            return;
        this.lastKeyFrameRequestTime = now;

        infoLog?.log(`requestKeyFrame: stream=${this.streamId}`);
        streamingApi.liveVideoStreams.RequestKeyFrame(RPC_SESSION_DEFAULT, this.streamId)
            .catch((e: unknown) => warnLog?.log('RequestKeyFrame error:', e));
    }

    private armLiveKeyframeGate(reason: string, minimumOffsetMs: number): void {
        const thresholdMs = Math.max(0, minimumOffsetMs);
        this.skippedBacklogFrames = 0;
        this.skipFramesBelowOffsetMs = Math.max(this.skipFramesBelowOffsetMs, thresholdMs);
        this.waitingForKeyframe = true;

        while (!this.pendingFrames.isEmpty()) {
            try { this.pendingFrames.shift()!.close(); } catch { /* already closed */ }
        }

        this.playbackStartTime = 0;
        this.lastSeekTime = 0;
        this.lastRenderedOffsetMs = 0;
        this.lastArrivedOffsetMs = 0;
        this.pipelineLatencyEma.reset();
        this.bufferDurationMsEma.reset();
        this.decoderQueueDepthEma.reset();
        this.pipelineLatencyMs = 0;

        if (this.decoderWorker) {
            void this.decoderWorker.flagWaitingForKeyframe()
                .catch((e: unknown) => warnLog?.log('flagWaitingForKeyframe error:', e));
        }
        this.requestKeyFrame();
        infoLog?.log(
            `armLiveKeyframeGate(${reason}): threshold=${this.skipFramesBelowOffsetMs.toFixed(0)}ms`);
    }

    private onVisibilityRestored(): void {
        if (!this.decoderWorker) return;
        this.restartAfterVisibilityChange();
    }

    private restartAfterVisibilityChange(): void {
        if (!this.decoderWorker) return;

        this.skippedBacklogFrames = 0;
        const pendingCount = this.pendingFrames.length;

        // Server-only skip architecture: keep the existing pull running, do
        // NOT call startPull (which forces a forward jump on the server and
        // destroys frames between currentOffset and now). Just gate deltas
        // until the PLI keyframe arrives in-band.
        const liveOffsetMs = Math.max(0, ServerClock.now() - this.startedAtMs);
        this.armLiveKeyframeGate(
            'visibility-restore',
            Math.max(0, liveOffsetMs - VIDEO.targetBufferDurationMs));

        // Reset timing anchor so playback re-syncs on next rendered frame
        this.rebufferDelayMs = 300;

        // Chrome may have throttled the decoder while hidden — give it a
        // warmup window before SLOW_DECODE thresholds re-arm.
        this.codecSlowTickCount = 0;
        this.decoderWarmupUntilMs = performance.now() + SLOW_DECODE_WARMUP_MS;

        this.lastDiagDecodedFrames = 0;
        this.lastDiagReceivedFrames = 0;
        this.receivedFrameCount = 0;
        this.receivedBytes = 0;
        this.firstFrameReceivedTime = 0;

        this.pipelineLatencyEma.reset();
        this.bufferDurationMsEma.reset();
        this.decoderQueueDepthEma.reset();
        this.pipelineLatencyMs = 0;
        infoLog?.log(
            `Tab restored: flushed ${pendingCount} pending frames, gating deltas until next keyframe`);
    }

    /** Called by Blazor */
    public async startPull(streamId: string, skipToMs: number): Promise<void> {
        if (!this.isPlaying) {
            warnLog?.log('startPull called but player not started');
            return;
        }

        infoLog?.log(
            `startPull:streamId=${streamId}, skipToMs=${skipToMs.toFixed(0)}, ` +
            `pullRetryCount=${this.pullRetryCount}, offThreadPullActive=${this.offThreadPullActive}, ` +
            `bgOffscreenTransferred=${this.bgOffscreenTransferred}, renderBackend=${this.renderBackend.kind}, ` +
            `isOffThread=${this.renderBackend.isOffThread}`);

        // Wait for the decoder worker to finish initialization before opening
        // the pull. Without this gate, frames arriving on the main-thread RPC
        // fallback path are dropped at `pushFrame` (`!this.decoderWorker`) —
        // and since `waitingForKeyframe` then waits for the next IDR, startup
        // stalls for a full keyframe interval. If the player was stopped while
        // we awaited, the abort/`_isPlayingNow` checks in the pull loop below
        // catch that and tear the stream down on the first iteration.
        await this.decoderReady;

        // Off-thread path: hand the entire Fusion RPC pull to the decoder worker.
        // Main thread becomes silent on the per-frame path.
        if (this.renderBackend.isOffThread && !this.offThreadPullActive && this.decoderWorker) {
            infoLog?.log(`startPull:entering delegatePullToWorker (first off-thread setup)`);
            const ok = await this.delegatePullToWorker(streamId, skipToMs);
            if (ok) return;
            // Worker rejected (no MSTG/VTG) — fall back to main-thread canvas + pull.
            warnLog?.log('Off-thread pull unavailable in worker — falling back to canvas + main-thread pull');
            (this.renderBackend as { dispose: () => void }).dispose();
            this.renderBackend = new CanvasRenderBackend(this.canvas);
            // Re-toggle inline display so canvas is visible and <video> is hidden.
            this.applyBackendVisibility(this.canvas, this.videoEl);
            // start() was already called by Blazor before startPull, so isPlaying is true here.
            this.startRenderLoop();
            // Fall through to existing main-thread pull below.
        }

        // DIAG: warn if we are about to run a main-thread pull while the off-thread
        // worker pull is still flagged active — indicates a retry path that bypasses
        // the off-thread fast lane and risks rendering nowhere (drawFrame is a no-op
        // on OffThreadRenderBackend).
        if (this.offThreadPullActive && this.renderBackend.isOffThread) {
            warnLog?.log(
                `startPull:SUSPICIOUS — main-thread pull starting while ` +
                `offThreadPullActive=true and renderBackend=${this.renderBackend.kind}. ` +
                `Decoded frames may not reach the visible <video>.`);
        }

        if (!this.renderBackend.isOffThread && skipToMs > VIDEO.targetBufferDurationMs) {
            const minimumOffsetMs = Math.max(0, skipToMs - VIDEO.targetBufferDurationMs);
            this.armLiveKeyframeGate('startPull-canvas', minimumOffsetMs);
        }

        // Cancel any existing pull
        this.pullAbortController?.abort();
        const abortController = new AbortController();
        this.pullAbortController = abortController;

        const skipToTicks = secondsToMoment(skipToMs / 1000);

        infoLog?.log(`startPull:stream=${streamId}, skipTo=${skipToMs}ms, skipToTicks=${skipToTicks}, retryCount=${this.pullRetryCount}`);

        try {
            infoLog?.log(`startPull:calling GetStream(${streamId}, ${skipToTicks})`);
            const stream = await streamingApi.liveVideoStreams.GetStream(RPC_SESSION_DEFAULT, streamId, skipToTicks);
            infoLog?.log(`startPull:GetStream returned, starting iteration`);
            let pullFrameCount = 0;

            for await (const frame of stream) {
                if (abortController.signal.aborted || !this._isPlayingNow) break;
                pullFrameCount++;
                this.pullRetryCount = 0;
                this.processRpcFrame(frame);
            }

            if (!abortController.signal.aborted && this._isPlayingNow) {
                if (pullFrameCount > 0) {
                    // Normal completion with frames — sender intentionally ended the stream
                    infoLog?.log(
                        `Pull stream completed normally after ${pullFrameCount} frames — treating as intentional end`);
                    void this.reportEnded();
                } else {
                    // Empty stream — skipTo may exceed available data, retry
                    warnLog?.log(
                        `Pull stream completed with 0 frames — skipTo may exceed available data, retrying at live edge`);
                    this.pullRetryCount++;
                    const delay = Math.min(500 * this.pullRetryCount, 2000);
                    warnLog?.log(
                        `Pull stream retry #${this.pullRetryCount}, delay ${delay}ms`);
                    this.pullRetryTimer = setTimeout(() => {
                        this.pullRetryTimer = null;
                        if (!this.isPlaying) return;
                        this.pullRetryCount = 0;
                        const retrySkipToMs = ServerClock.now() - this.startedAtMs;
                        void this.startPull(streamId, retrySkipToMs);
                    }, delay);
                }
            }
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            const stack = err instanceof Error ? err.stack : '';
            warnLog?.log(`startPull: error,${message}`, stack);
            if (abortController.signal.aborted || !this._isPlayingNow) return;
            this.pullRetryCount++;
            const delay = Math.min(1000 * this.pullRetryCount, 5000);
            warnLog?.log(
                `Pull stream error (retry #${this.pullRetryCount}, delay ${delay}ms): ${message}`);
            this.pullRetryTimer = setTimeout(() => {
                this.pullRetryTimer = null;
                if (!this.isPlaying) return;
                this.pullRetryCount = 0;
                const retrySkipToMs = ServerClock.now() - this.startedAtMs;
                void this.startPull(streamId, retrySkipToMs);
            }, delay);
        }
    }

    private processRpcFrame(frame: VideoFrameDto): void {
        try {
            const offsetMs = momentToSeconds(frame.Offset) * 1000;   // .NET ticks -> ms
            const durationMs = momentToSeconds(frame.Duration) * 1000;
            const isKeyFrame = frame.IsKeyFrame;
            const data = frame.Data;
            const description = frame.Description ?? undefined;

            this.receivedFrameCount++;
            this.receivedBytes += data.byteLength;
            if (frame.SpatialLayerId !== undefined)
                this.forwardedSpatialLayerId = frame.SpatialLayerId;
            if (frame.MaxSpatialLayerId !== undefined && frame.MaxSpatialLayerId > this.observedMaxSpatialLayer)
                this.observedMaxSpatialLayer = frame.MaxSpatialLayerId;
            if (frame.Width !== undefined && frame.Width > 0)
                this.forwardedWidth = frame.Width;
            if (frame.Height !== undefined && frame.Height > 0)
                this.forwardedHeight = frame.Height;
            if (this.firstFrameReceivedTime === 0)
                this.firstFrameReceivedTime = performance.now();
            if (offsetMs > this.lastArrivedOffsetMs)
                this.lastArrivedOffsetMs = offsetMs;
            if (isKeyFrame) {
                this.receivedKeyframeCount++;
            } else if (this.receivedFrameCount % 100 === 1) {
                debugLog?.log(
                    `processRpcFrame #${this.receivedFrameCount}: offsetMs=${offsetMs.toFixed(0)}, ` +
                    `durationMs=${durationMs.toFixed(1)}, dataLen=${data.length}`);
            }

            // Diagnostic: log implied latency for first 5 frames, every 300th, and during high latency
            const nowMs = ServerClock.now();
            const impliedCaptureAt = this.startedAtMs + offsetMs;
            const impliedLatency = nowMs - impliedCaptureAt;
            const isHighLatency = impliedLatency > 2000
                && (performance.now() - this.lastHighLatencyLogTime > 1000);
            if (this.receivedFrameCount <= 5 || this.receivedFrameCount % 300 === 0 || isHighLatency) {
                if (isHighLatency) this.lastHighLatencyLogTime = performance.now();
                warnLog?.log(
                    `processRpcFrame: #${this.receivedFrameCount} offsetMs=${offsetMs.toFixed(0)}, ` +
                    `startedAt=${this.startedAtMs.toFixed(0)}, impliedCaptureAt=${impliedCaptureAt.toFixed(0)}, ` +
                    `serverNow=${nowMs.toFixed(0)}, impliedLatency=${impliedLatency.toFixed(0)}ms, isKey=${isKeyFrame}`);
            }

            this.pushFrame(data, offsetMs, durationMs, isKeyFrame, description, frame.Width, frame.Height);
        } catch (error) {
            errorLog?.log('Error processing received frame:', error);
        }
    }

    public stopPull(): void {
        if (this.pullRetryTimer !== null) {
            clearTimeout(this.pullRetryTimer);
            this.pullRetryTimer = null;
        }
        if (this.pullAbortController) {
            this.pullAbortController.abort();
            this.pullAbortController = null;
        }
    }

    public async getDiagnosticsAsync(): Promise<RemoteStreamDiagnostics> {
        let decoderStats: DecoderStats | null = null;
        if (this.decoderWorker) {
            try { decoderStats = await this.decoderWorker.getStats(); } catch { /* ignore */ }
        }

        // Compute incoming bitrate. In off-thread pull mode the worker owns
        // the pull loop, so main-thread receivedBytes / receivedFrameCount /
        // receivedKeyframeCount stay at 0 — read pull stats from the worker
        // instead. Fall back to local counters for the main-thread pull path.
        const workerPull = this.offThreadPullActive
            && decoderStats?.pullReceivedFrameCount !== undefined
            ? decoderStats : null;
        let bitrateKbps: number;
        let receivedFrameCount: number;
        let receivedKeyframeCount: number;
        if (workerPull) {
            bitrateKbps = workerPull.pullBitrateKbps ?? 0;
            receivedFrameCount = workerPull.pullReceivedFrameCount ?? 0;
            receivedKeyframeCount = workerPull.pullReceivedKeyframeCount ?? 0;
        } else {
            const elapsedSec = this.firstFrameReceivedTime > 0
                ? (performance.now() - this.firstFrameReceivedTime) / 1000
                : 0;
            bitrateKbps = elapsedSec > 0
                ? Math.round(this.receivedBytes * 8 / elapsedSec / 1000)
                : 0;
            receivedFrameCount = this.receivedFrameCount;
            receivedKeyframeCount = this.receivedKeyframeCount;
        }

        // A/V drift was computed against AudioVideoSync; the hub has been
        // removed, so report null until the new video-driven catch-up signal
        // is wired (see docs/audio-pipeline-wip.md).
        const avDriftMs: number | null = null;
        const requested = requestedReceiveQuality.get(this.streamId) ?? null;
        const streamAgeMs = this.firstFrameReceivedTime > 0
            ? Math.round(performance.now() - this.firstFrameReceivedTime)
            : 0;
        const forwardedSpatialLayerId = this.forwardedSpatialLayerId >= 0
            ? this.forwardedSpatialLayerId
            : decoderStats?.pullForwardedSpatialLayerId ?? -1;
        const forwardedWidth = this.forwardedSpatialLayerId >= 0
            ? this.forwardedWidth
            : decoderStats?.pullForwardedWidth ?? 0;
        const forwardedHeight = this.forwardedSpatialLayerId >= 0
            ? this.forwardedHeight
            : decoderStats?.pullForwardedHeight ?? 0;
        const observedMaxSpatialLayer = this.observedMaxSpatialLayer >= 0
            ? this.observedMaxSpatialLayer
            : decoderStats?.pullObservedMaxSpatialLayer ?? -1;

        return {
            streamId: this.streamId,
            authorId: this.authorId,
            codec: this.decoderConfig?.codec ?? 'unknown',
            codecCategory: this.codecCategory,
            bitrateKbps,
            pipelineLatencyMs: Math.round(this.pipelineLatencyMs),
            jitterBufferMs: JITTER_BUFFER_MS,
            jitterEstimateMs: 0,
            smoothedRttMs: Math.round(this.smoothedRttMs),
            rttGradientMs: Math.round(this.rttGradientMs),
            playbackRate: 1.0,
            // Encoded pre-decode buffer depth (the doc's `video buffer`),
            // surfaced from the decoder worker's getStats. Decoded slot is
            // single-frame so its own depth is uninteresting.
            bufferSize: decoderStats?.encodedBufferDepth ?? 0,
            receivedFrameCount,
            receivedKeyframeCount,
            renderFrameCount: this.renderFrameCount,
            skipToLiveCount: this.skipToLiveCount,
            waitingForKeyframe: this.waitingForKeyframe,
            qualityReductionRequested: this.qualityReductionRequested,
            codecSlowTickCount: this.codecSlowTickCount,
            decoderStats,
            avDriftMs,
            forwarded: forwardedSpatialLayerId >= 0 ? {
                ForwardedSpatialLayerId: forwardedSpatialLayerId,
                ForwardedWidth: forwardedWidth,
                ForwardedHeight: forwardedHeight,
                ObservedMaxSpatialLayer: observedMaxSpatialLayer,
            } : null,
            requestedReceiveQuality: requested,
            streamAgeMs,
        };
    }

    // Hands the entire Fusion RPC pull to the decoder worker. Returns true on
    // success, false if neither tier of off-thread setup works — caller falls
    // back to canvas + main-thread pull.
    //
    // Two-tier setup. Tier 2 first: if main-thread globalThis exposes
    // MediaStreamTrackGenerator (Chromium today), construct it here, attach the
    // track to the <video> immediately, and ship the writable to the worker.
    // Tier 1: if no main MSTG, let the worker try to construct MSTG/VTG itself
    // (Safari workers, future Chromium worker MSTG). The worker rejects when
    // neither tier yields a writable.
    private async delegatePullToWorker(streamId: string, skipToMs: number): Promise<boolean> {
        if (!this.decoderWorker) return false;

        this.delegateEntryCount++;
        infoLog?.log(
            `delegatePullToWorker:entry #${this.delegateEntryCount}, ` +
            `streamId=${streamId}, skipToMs=${skipToMs.toFixed(0)}, ` +
            `bgOffscreenTransferred=${this.bgOffscreenTransferred}, ` +
            `renderBackend.kind=${this.renderBackend.kind}`);

        let mainGenerator: MediaStreamTrack | null = null;
        let mainWritable: WritableStream<VideoFrame> | undefined;
        const Ctor = (globalThis as unknown as {
            MediaStreamTrackGenerator?: new (init: { kind: 'video' }) => MediaStreamTrack & { readonly writable: WritableStream<VideoFrame> };
        }).MediaStreamTrackGenerator;
        if (typeof Ctor === 'function') {
            try {
                const generator = new Ctor({ kind: 'video' });
                mainGenerator = generator;
                mainWritable = generator.writable;
                const backend = this.renderBackend as { onTrackReady?: (t: MediaStreamTrack) => void };
                if (typeof backend.onTrackReady === 'function')
                    backend.onTrackReady(generator);
                infoLog?.log(
                    `delegatePullToWorker:Tier 2 main-thread MSTG constructed, ` +
                    `trackId=${generator.id}, attached via backend=${this.renderBackend.kind}`);
            } catch (e) {
                warnLog?.log('Main-thread MSTG construct failed, falling back to worker tier:', e);
                mainGenerator = null;
                mainWritable = undefined;
            }
        } else {
            infoLog?.log(`delegatePullToWorker:Tier 2 unavailable (no globalThis.MediaStreamTrackGenerator), falling back to worker tier`);
        }

        const apiUrl = BrowserInit.getUrl('/rpc/ws').replace(/^http/, 'ws');
        SharedSettings.update({ apiUrl });
        // Hand the bg canvas to the worker so it can paint a low-res blurred
        // backdrop directly (see §13). transferControlToOffscreen() can be
        // called only once per element across the lifetime of the document —
        // guard so a re-pull doesn't try to re-transfer.
        let bgOffscreen: OffscreenCanvas | undefined;
        if (!this.bgOffscreenTransferred &&
            typeof (this.bgCanvasEl as { transferControlToOffscreen?: () => OffscreenCanvas })
                .transferControlToOffscreen === 'function') {
            try {
                bgOffscreen = this.bgCanvasEl.transferControlToOffscreen();
                this.bgOffscreenTransferred = true;
                infoLog?.log(`delegatePullToWorker:bgCanvas transferred to worker (first time)`);
            } catch (e) {
                warnLog?.log('transferControlToOffscreen failed for bg canvas:', e);
            }
        } else {
            infoLog?.log(
                `delegatePullToWorker:bgCanvas NOT transferred this call ` +
                `(alreadyTransferred=${this.bgOffscreenTransferred}, ` +
                `transferFn=${typeof (this.bgCanvasEl as { transferControlToOffscreen?: unknown }).transferControlToOffscreen})`);
        }
        try {
            await this.decoderWorker.startPullInWorker(
                streamId, skipToMs, apiUrl,
                this.startedAtMs, JITTER_BUFFER_MS,
                mainWritable,
                bgOffscreen);
            this.offThreadPullActive = true;
            debugLog?.log(`Off-thread pull started for ${streamId}, skipTo=${skipToMs}ms (tier ${mainWritable ? 2 : 1})`);
            return true;
        } catch (e) {
            warnLog?.log('startPullInWorker rejected:', e);
            if (mainGenerator) {
                try { mainGenerator.stop(); } catch { /* ignore */ }
            }
            return false;
        }
    }

    private onWorkerLatencyReport(report: DecoderWorkerLatencyReport): void {
        // Refresh the output-verification reference BEFORE running the
        // check below — off-thread mode learns latest keyframe dims via
        // this report, not via pushFrame.
        if (report.lastKeyframeWidth && report.lastKeyframeHeight) {
            this.lastKeyframeWidth = report.lastKeyframeWidth;
            this.lastKeyframeHeight = report.lastKeyframeHeight;
        }
        if (this.checkOutputVerification('worker-latency'))
            return;
        const streamOffsetMs = report.streamOffsetMs;
        if (streamOffsetMs > this.lastArrivedOffsetMs)
            this.lastArrivedOffsetMs = streamOffsetMs;
        if (report.presentedOffsetMs !== undefined) {
            this.lastRenderedOffsetMs = report.presentedOffsetMs;
            this.reportPresentationLag(report.presentedOffsetMs, 'worker-latency-report');
        }
        if (this.offThreadPullActive && this.decoderWorker) {
            void this.decoderWorker.getStats()
                .then(ds => this.reportPlaybackHealth(ds, Math.max(0, report.bufferSpanMs)))
                .catch((e: unknown) => warnLog?.log('onWorkerLatencyReport getStats error:', e));
        }
        // Latency reports formerly went to streamServer.ReportVideoLatency; the
        // playback quality controller (Step 10) now consumes equivalent signals
        // via ChangePlaybackQuality. This handler still updates lastArrivedOffsetMs
        // and other local state read by the render loop.
    }

    public async stop(): Promise<void> {
        if (!this.isPlaying) return;

        infoLog?.log(`VideoPlayer stop() called for stream ${this.streamId}, rendered=${this.renderFrameCount} frames, received=${this.receivedFrameCount}`);

        // Unregister from global diagnostics registry
        activePlayers.delete(this.streamId);
        infoLog?.log(`VideoPlayer registry: removed ${this.streamId}, active=${activePlayers.size}`);

        this.isPlaying = false;
        this.stopRenderLoop();
        this.stopOutputVerificationMonitor();
        Api.releaseConnection(`VideoPlayer:${this.streamId}`);
        this.playbackStartTime = 0;
        this.lastRenderedOffsetMs = 0;
        this.lastArrivedOffsetMs = 0;
        this.renderFrameCount = 0;
        this.receivedFrameCount = 0;
        this.receivedKeyframeCount = 0;
        this.receivedBytes = 0;
        this.firstFrameReceivedTime = 0;
        this.pipelineLatencyEma.reset();
        this.bufferDurationMsEma.reset();
        this.decoderQueueDepthEma.reset();
        this.pipelineLatencyMs = 0;
        this.skipFramesBelowOffsetMs = 0;
        this.skippedBacklogFrames = 0;
        this.lastDiagDecodedFrames = 0;
        this.lastDiagReceivedFrames = 0;
        this.consecutiveEmptyRenders = 0;
        this.lastSeekTime = 0;
        this.pullRetryCount = 0;
        this.lastLatencyReportTime = 0;

        // Remove visibility subscription
        if (this.visibilitySubscription) {
            this.visibilitySubscription.unsubscribe();
            this.visibilitySubscription = null;
        }

        // Disconnect the canvas resize observer
        if (this.resizeObserver) {
            this.resizeObserver.disconnect();
            this.resizeObserver = null;
        }

        this.stopPull();

        // Off-thread cleanup
        if (this.offThreadPullActive && this.decoderWorker) {
            try { void this.decoderWorker.stopPullInWorker(rpcNoWait); } catch { /* ignore */ }
            this.offThreadPullActive = false;
        }
        if (this.connectivityHandlerOnline) {
            ConnectivityUI.isOnlineChanged.remove(this.connectivityHandlerOnline);
            this.connectivityHandlerOnline = null;
        }
        if (this.connectivityHandlerConnected) {
            ConnectivityUI.isConnectedChanged.remove(this.connectivityHandlerConnected);
            this.connectivityHandlerConnected = null;
        }
        if (this.sharedSettingsRegistration) {
            this.sharedSettingsRegistration.dispose();
            this.sharedSettingsRegistration = null;
        }

        // Close all pending frames
        while (!this.pendingFrames.isEmpty()) {
            try {
                this.pendingFrames.shift()!.close();
            } catch {
                // Ignore
            }
        }

        // Close stream input channel
        if (this.chunkInputChannel) {
            try { void this.chunkInputChannel.writer.close(); } catch { /* ignore */ }
            this.chunkInputChannel = null;
        }

        // Stop decoder worker
        if (this.decoderWorker) {
            try {
                await this.decoderWorker.stop();
            } catch {
                // Ignore
            }
            this.decoderWorker.dispose();
            this.decoderWorker = null;
        }
        if (this.decoderWorkerInstance) {
            this.decoderWorkerInstance.terminate();
            this.decoderWorkerInstance = null;
        }

        this.renderBackend.dispose();

        debugLog?.log(`VideoPlayer stopped for stream ${this.streamId}`);
    }

    private reportLatencyTick(): void {
        if (!this.isPlaying)
            return;

        // Chrome throttles requestAnimationFrame / setTimeout heavily in hidden
        // tabs (rAF → ~1 Hz, timers → ≥1s clamp). `lastRenderedOffsetMs` stops
        // advancing while wall-clock keeps ticking → computed latency balloons
        // → spurious SKIP_TO_LIVE fires the moment a throttled tick lands. The
        // onVisibilityRestored path (visibilityChange handler) already issues a
        // fresh PLI + stream re-request, so skipping latency reporting while
        // hidden is safe recovery and avoids double-triggering.
        if (document.hidden)
            return;

        if (this.lastRenderedOffsetMs <= 0) {
            infoLog?.log(`reportLatencyTick: skip — lastRendered=${this.lastRenderedOffsetMs.toFixed(0)}`);
            return;
        }
        const nowMs = ServerClock.now();
        // Two metrics with distinct semantics:
        // - latencyMs (newest arrived frame vs now) = true sender→receiver transit.
        //   Used for SKIP_TO_LIVE trigger and for user-visible "network latency".
        // - frameAgeMs (rendered frame vs now) = how old is what's on screen. High on
        //   screencast with sparse heartbeats (up to heartbeat interval) even when
        //   transit is tiny — content just hasn't changed recently. Diagnostic only.
        const arrivedAtMs = this.startedAtMs + this.lastArrivedOffsetMs;
        const renderedAtMs = this.startedAtMs + this.lastRenderedOffsetMs;
        const latencyMs = nowMs - arrivedAtMs;
        const frameAgeMs = nowMs - renderedAtMs;
        // Presentation lag at the canvas, in source-time terms (startedAtMs is
        // the source's claimed start time). Audio catch-up policy compares this
        // against the audio-side equivalent measured at the speaker. Webcam
        // streams only — screencast lag is filtered out by the .NET handler.
        const sysNow = ServerClock.now();
        const presentationLagMs = sysNow - renderedAtMs;
        this.reportPresentationLag(this.lastRenderedOffsetMs, 'canvas-latency-report', presentationLagMs);
        infoLog?.log(
            `reportLatencyTick: authorId=${this.authorId}, streamId=${this.streamId}, ` +
            `now=${nowMs.toFixed(0)}, arrivedAt=${arrivedAtMs.toFixed(0)} ` +
            `(startedAt=${this.startedAtMs.toFixed(0)}+arrivedOffset=${this.lastArrivedOffsetMs.toFixed(0)}), ` +
            `latency=${latencyMs.toFixed(0)}ms, frameAge=${frameAgeMs.toFixed(0)}ms ` +
            `(renderedOffset=${this.lastRenderedOffsetMs.toFixed(0)})`);

        // Audio-sync catch-up: when render age grows, reduce pipelineLatencyMs to
        // advance the audio-sync target. Uses frameAgeMs because pipelineLatencyMs
        // tracks render delay — the same domain as frameAge, not network transit.
        if (frameAgeMs > CATCHUP_GENTLE_MS && frameAgeMs <= DROP_TO_KEYFRAME_MS) {
            const excessMs = frameAgeMs - CATCHUP_GENTLE_MS;
            const reductionMs = Math.min(excessMs * 0.3, 20); // Reduce by up to 20ms per tick
            if (this.pipelineLatencyMs > reductionMs) {
                // Out-of-band override: lower the EMA's smoothed estimate so
                // subsequent samples blend forward from the new baseline.
                this.pipelineLatencyEma.setValue(this.pipelineLatencyMs - reductionMs);
                this.pipelineLatencyMs = this.pipelineLatencyEma.value;
                warnLog?.log(
                    `reportLatencyTick: catchup, frameAge=${frameAgeMs.toFixed(0)}ms, reducing pipelineLatencyMs by ${reductionMs.toFixed(1)}ms to ${this.pipelineLatencyMs.toFixed(0)}ms`);
            }
        }

        // Cooldown: after SKIP_TO_LIVE, give the new stream time to stabilize
        if (performance.now() - this.lastSkipToLiveTime < 5000)
            return;

        // Graduated recovery: when rendered-frame age is high, buffered frames are
        // stale. Dropping the oldest half helps the renderer reach live without
        // ratcheting through aged content. Uses frameAgeMs (render-domain signal).
        if (frameAgeMs > DROP_TO_KEYFRAME_MS && frameAgeMs <= VIDEO.skipToLiveThresholdMs) {
            // Phase 2: Drop oldest frames to catch up quickly.
            // PendingFrame (decoded VideoFrame/ImageBitmap) lacks isKeyFrame metadata,
            // so we can't do keyframe-aware dropping — drop the oldest half instead.
            const dropCount = Math.floor(this.pendingFrames.length / 2);
            if (dropCount > 0) {
                warnLog?.log(
                    `reportLatencyTick: graduated recovery, frameAge=${frameAgeMs.toFixed(0)}ms > ${DROP_TO_KEYFRAME_MS}ms, dropping ${dropCount} oldest frames`);
                for (let i = 0; i < dropCount; i++) {
                    this.pendingFrames.shift()!.close();
                }
            }
        }
        // SKIP_TO_LIVE triggers on NETWORK latency (latencyMs = arrival vs sender),
        // not frameAge — on screencast, frameAge can hit 1.5s just from heartbeat
        // pacing on a perfectly healthy link, and re-requesting the stream would
        // be pointless churn. Arrival latency only grows when the stream is actually
        // stalled server-side or the network is congested.
        else if (latencyMs > VIDEO.skipToLiveThresholdMs) {
            // Server-only skip architecture: don't issue a fresh GetVideo
            // (which would skip server-side and destroy frames between
            // currentOffset and now). Notify the server of the latency, ask
            // for a keyframe, and gate deltas at the worker until it arrives.
            this.skipToLiveCount++;
            warnLog?.log(
                `reportLatencyTick: skip-to-live, latency=${latencyMs.toFixed(0)}ms > ${VIDEO.skipToLiveThresholdMs}ms, gating until next keyframe (count=${this.skipToLiveCount})`);

            while (!this.pendingFrames.isEmpty())
                this.pendingFrames.shift()!.close();
            this.pipelineLatencyEma.reset();
            this.bufferDurationMsEma.reset();
            this.decoderQueueDepthEma.reset();
            this.pipelineLatencyMs = 0;
            this.playbackStartTime = 0;
            this.lastRenderedOffsetMs = 0;
            this.lastArrivedOffsetMs = 0;
            this.lastSkipToLiveTime = performance.now();
            // Stale arrival times from before the gate are meaningless

            if (this.decoderWorker) {
                void this.decoderWorker.flagWaitingForKeyframe();
            }
            this.waitingForKeyframe = true;
            this.requestKeyFrame();
            return;
        }

        // Collect decoder diagnostics and send enriched latency report
        if (this.decoderWorker) {
            void this.decoderWorker.getStats().then(ds => {
                if (this.checkOutputVerification('decoder-stats'))
                    return;
                const recvDelta = this.receivedFrameCount - this.lastDiagReceivedFrames;
                const decodedDelta = ds.decodedFrames - this.lastDiagDecodedFrames;
                this.lastDiagReceivedFrames = this.receivedFrameCount;
                this.lastDiagDecodedFrames = ds.decodedFrames;

                // Compute buffer span (time range of buffered frames)
                let currentBufferSpanMs = 0;
                if (this.pendingFrames.length >= 2) {
                    currentBufferSpanMs = (this.pendingFrames.peekBack()!.timestamp
                        - this.pendingFrames.peekFront()!.timestamp) / 1000;
                }
                const bufferDurationMs = Math.round(ds.encodedBufferSpanMs ?? currentBufferSpanMs);
                this.reportPlaybackHealth(ds, bufferDurationMs);

                infoLog?.log(
                    `reportLatencyTick: decode, codec=${this.decoderConfig?.codec ?? 'unknown'} ` +
                    `decode=${ds.pureMedianDecodeTime >= 0 ? ds.pureMedianDecodeTime.toFixed(1) : 'N/A'}ms ` +
                    `queueWait=${ds.medianDecodeTime.toFixed(1)}ms ` +
                    `queueDepth=${ds.decodeQueueSize} bpDrops=${ds.backpressureDrops} ` +
                    `e2e=${this.pipelineLatencyMs.toFixed(0)}ms buf=${this.pendingFrames.length} ` +
                    `bufSpanMs=${currentBufferSpanMs.toFixed(0)} ` +
                    `recv=${recvDelta} decoded=${decodedDelta} drop=${ds.droppedFrames} ` +
                    `res=${ds.resolution} hw=${ds.hardwareAcceleration}`);

                // Decode performance tracking — detect codecs that can't sustain realtime.
                // Skip when:
                //  - within warmup window: codec init + first KF latency dominate the median
                //    and don't repeat at steady state (typical: 200–600 ms cold, < 1 ms hot).
                //  - tab is hidden: rAF stops on the main thread, decoded-frame queue swells,
                //    looks like decoder slowness but is just paused consumption (mirror of
                //    the sender-side hidden-tab encoder backpressure case).
                const inWarmup = performance.now() < this.decoderWarmupUntilMs;
                const tabHidden = typeof document !== 'undefined' && document.visibilityState === 'hidden';
                const isBadTick = !inWarmup && !tabHidden
                    && (ds.medianDecodeTime > VIDEO.highDecodeTimeThresholdMs
                        || ds.decodeQueueSize > VIDEO.highBufferDepthThreshold);
                if (inWarmup || tabHidden) {
                    if (this.codecSlowTickCount > 0) {
                        debugLog?.log(
                            `reportLatencyTick: slow-decode (${inWarmup ? 'warmup' : 'hidden tab'}) — ` +
                            `resetting tick count (was ${this.codecSlowTickCount})`);
                        this.codecSlowTickCount = 0;
                    }
                }
                if (isBadTick) {
                    this.codecSlowTickCount++;
                    if (!this.qualityReductionRequested && this.codecSlowTickCount >= QUALITY_REDUCTION_TICK_COUNT) {
                        // Phase 1: request quality reduction from the sender
                        warnLog?.log(
                            `reportLatencyTick: slow-decode, ${this.codecSlowTickCount} consecutive bad ticks for ${this.codecCategory}, ` +
                            `requesting quality reduction (medianDecode=${ds.medianDecodeTime.toFixed(1)}ms, ` +
                            `queueDepth=${ds.decodeQueueSize})`);
                        this.qualityReductionRequested = true;
                        this.codecSlowTickCount = 0; // reset, give reduced quality time to take effect
                        void this.blazorRef.invokeMethodAsync('OnRequestQualityReduction', this.codecCategory);
                    } else if (this.qualityReductionRequested && this.codecSlowTickCount >= CODEC_EXCLUSION_TICK_COUNT
                        && this.codecCategory !== 'h264' && this.codecCategory !== 'unknown') {
                        // Phase 2: quality reduction didn't help — exclude codec entirely
                        warnLog?.log(
                            `reportLatencyTick: slow-decode, codec ${this.codecCategory} too slow even after quality reduction ` +
                            `(${this.codecSlowTickCount} more bad ticks), requesting codec exclusion`);
                        void this.blazorRef.invokeMethodAsync('OnRequestCodecExclusion', this.codecCategory);
                        void this.reportEnded('Codec excluded after sustained slow decode');
                        return;
                    }
                } else {
                    if (this.codecSlowTickCount > 0) {
                        debugLog?.log(`reportLatencyTick: slow-decode reset — good tick after ${this.codecSlowTickCount} bad ticks`);
                    }
                    this.codecSlowTickCount = 0;
                    this.qualityReductionRequested = false;
                }

                // ReportVideoLatency removed in Step 8.4. The playback quality
                // controller (Step 10) consumes equivalent decoder + buffer
                // signals via ChangePlaybackQuality. RTT measurement returns
                // when the new flow lands.
            });
        }
    }

    private reportPresentationLag(renderedOffsetMs: number, source: string, presentationLagMs?: number): void {
        const lagMs = presentationLagMs ?? ServerClock.now() - (this.startedAtMs + renderedOffsetMs);
        void this.blazorRef.invokeMethodAsync('OnPresentationLag', lagMs)
            .catch(() => { /* ignore */ });
    }

    // Maps this player's current render size to a VideoQualityLevel hint for the
    // server's simulcast fan-out. Uses CSS layout pixels rather than canvas.width
    // (decoder output resolution). Server maps Low→spatial layer 0,
    // Medium→1, High/Full/Ultra→2.
    private computeRenderQualityLevel(): number | null {
        const parent = this.canvas.parentElement;
        const canvasWidth = this.canvas.clientWidth;
        const parentWidth = parent?.clientWidth ?? 0;
        const parentRectWidth = parent?.getBoundingClientRect().width ?? 0;
        const width = canvasWidth > 0 ? canvasWidth
            : parentWidth > 0 ? parentWidth
                : parentRectWidth > 0 ? parentRectWidth
                    : 0;
        const level = renderQualityLevelForWidth(width);
        if (level !== null)
            return level;

        // During tab restore / layout transitions a small tile can briefly
        // report 0px width. Do not let that become "unknown/top quality":
        // PIP/sidebar webcam tiles are secondary and should stay at base layer.
        if (parent?.classList.contains('pip-overlay') || parent?.classList.contains('item-x'))
            return 4;

        // Focused or detached: leave uncapped until layout reports real size.
        return null;
    }

    private updateRttEstimate(rttMs: number): void {
        this.previousRttMs = this.smoothedRttMs;
        this.smoothedRttMs = this.smoothedRttMs === 0 ? rttMs : 0.8 * this.smoothedRttMs + 0.2 * rttMs;
        this.rttGradientMs = this.smoothedRttMs - this.previousRttMs;

        // Proactive congestion detection: RTT increasing rapidly
        if (this.rttGradientMs > 50 && this.smoothedRttMs > 100) {
            warnLog?.log(
                `updateRttEstimate: rtt-gradient, rtt=${this.smoothedRttMs.toFixed(0)}ms, gradient=${this.rttGradientMs.toFixed(0)}ms — congestion detected`);
            // Proactively request quality reduction before latency threshold is hit
            if (!this.qualityReductionRequested && this.codecCategory) {
                void this.blazorRef.invokeMethodAsync('OnRequestQualityReduction', this.codecCategory);
                this.qualityReductionRequested = true;
            }
        }
    }

    private reportPlaybackHealth(ds: DecoderStats, bufferDurationMs: number): void {
        const workerPull = this.offThreadPullActive && ds.pullReceivedFrameCount !== undefined ? ds : null;
        let bitrateKbps: number;
        if (workerPull) {
            bitrateKbps = workerPull.pullBitrateKbps ?? 0;
        } else {
            const elapsedSec = this.firstFrameReceivedTime > 0
                ? (performance.now() - this.firstFrameReceivedTime) / 1000
                : 0;
            bitrateKbps = elapsedSec > 0
                ? Math.round(this.receivedBytes * 8 / elapsedSec / 1000)
                : 0;
        }
        const renderLevel = this.computeRenderQualityLevel();
        const skipDelta = Math.max(0, this.skipToLiveCount - this.lastQualitySkipToLiveCount);
        this.lastQualitySkipToLiveCount = this.skipToLiveCount;
        this.bufferDurationMsEma.appendSample(Math.max(0, bufferDurationMs));
        this.decoderQueueDepthEma.appendSample(Math.max(0, ds.decodeQueueSize));
        // Wire fields are doubles on the C# side — no rounding needed; keep
        // sub-ms precision so the receiver-side classifier sees the smoothed
        // signal exactly as computed here.
        const snapshot: PlaybackHealthSnapshot = {
            incomingByteRate: Math.round(bitrateKbps * 1000 / 8),
            bufferDurationMsEma: this.bufferDurationMsEma.value,
            keyframeSkipsInWindow: skipDelta,
            decoderQueueDepthEma: this.decoderQueueDepthEma.value,
            currentMaxSpatial: maxSpatialForRenderQualityLevel(renderLevel),
            currentMaxTemporal: MAX_TEMPORAL_LAYER,
            priority: priorityForRenderQualityLevel(renderLevel),
            streamAgeMs: Math.max(0, Math.round(performance.now() - this.createdAtMs)),
            qualityReductionRequested: this.qualityReductionRequested,
            latencyMsEma: Math.max(0, this.pipelineLatencyMs),
        };
        void this.blazorRef.invokeMethodAsync('OnPlaybackHealth', snapshot)
            .catch((e: unknown) => warnLog?.log('reportPlaybackHealth error:', e));
    }

    private async reportPlaying(offsetMs: number, isBufferLow: boolean): Promise<void> {
        try {
            await this.blazorRef.invokeMethodAsync('OnPlaying', offsetMs, isBufferLow);
        } catch (e) {
            warnLog?.log('reportPlaying error:', e);
        }
    }

    private async reportEnded(error?: string): Promise<void> {
        try {
            debugLog?.log(`VideoPlayer reporting ended for stream ${this.streamId}:`, error);
            await this.blazorRef.invokeMethodAsync('OnEnded', error ?? null);
        } catch (e) {
            warnLog?.log('reportEnded error:', e);
        }
    }
}

function maxSpatialForRenderQualityLevel(level: number | null): number {
    if (level === null)
        return DEFAULT_MAX_SPATIAL_LAYER;
    if (level >= 4)
        return 0;
    if (level >= 3)
        return 1;
    return DEFAULT_MAX_SPATIAL_LAYER;
}

function priorityForRenderQualityLevel(level: number | null): number {
    return level === null || level <= 2
        ? PLAYBACK_PRIORITY_PRIMARY
        : PLAYBACK_PRIORITY_SECONDARY;
}

import { getLogs } from 'logging';
import { Api, streamingApi } from 'api';
import { delayAsync } from 'actuallab-core';
import { ServerClock } from 'clocks';

const RPC_SESSION_DEFAULT = '~';
import { rpcClientServer, rpcNoWait } from 'rpc';
import { SharedSettingsWorkerSync } from 'shared-settings-worker';
import type { Disposable } from 'disposable';
import { DocumentEvents } from 'event-handling';
import { Versioning } from 'versioning';
import { type Subscription } from 'rxjs';
import { updateCollapsedIslandAspect } from '../../Services/Video/services/tile-fit';
import type {
    PlayerWorker,
    LatencySample,
} from '../../Services/Video/playback/player-worker-contract';
import { getAudioLatency, isSkipToAudioEnabled } from '../../Services/Video/audio-latency-registry';
import type { RenderBackendKind } from '../../Services/Video/playback/render-backends';
import type { PlayerStats } from '../../Services/Video/frame-envelopes';
import {
    getCodecCandidates,
    selectDecoderCodec,
} from '../../Services/Video/hevc-codec-selection';
import { isDecoderCodecProven, markDecoderCodecProven } from '../../Services/Video/codec-support';
import { consumeVideoTraceKill, registerVideoTraceKillWorker } from '../../Services/Video/video-trace-kill-control';
import { isCodecExhaustedError } from '../../Services/Video/operators/decode';
import { ThroughputDeficitTicker } from '../../Services/Video/throughput-deficit-ticker';
import type { RenderBackend } from './render-backend';
import { TransferableCanvasRenderBackend } from './render-backend-canvas';
import { OffThreadRenderBackend } from './render-backend-mstg';
import { pickRenderBackendKind } from './render-backend-selection';
import type { BgBlurMode } from '../../Services/Video/playback/bg-blur-tap';
import { readBgBlurOverride } from '../../Services/Video/playback/bg-blur-override';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { ConnectivityUI } from '../../../UI.Blazor/Services/ConnectivityUI/connectivity-ui';
import { AC, VIDEO } from 'app-constants';
import { RunningEMA } from 'math';

// Backend selection: prefer the off-thread renderer wherever a generator API
// (MediaStreamTrackGenerator on Chromium, VideoTrackGenerator on Safari) is
// plausibly available. The worker host probes the real APIs; if none exists,
// it rejects start() and we fall back to canvas.
// ?renderBackend=mstg|canvas overrides for diagnostics.
function pickRenderBackend(canvas: HTMLCanvasElement, videoEl: HTMLVideoElement): RenderBackend {
    if (pickRenderBackendKind() === 'canvas')
        return new TransferableCanvasRenderBackend(canvas);
    return new OffThreadRenderBackend(videoEl);
}

// Global registry of active VideoPlayer instances for diagnostics

const activePlayers = new Map<string, VideoPlayer>();
export function getActivePlayers(): ReadonlyMap<string, VideoPlayer> {
    return activePlayers;
}

const requestedReceiveQuality = new Map<string, {
    layerId: number;
} | null>();

export function recordRequestedReceiveQuality(
    streamId: string,
    quality: { layerId: number } | null
): void {
    if (quality === null)
        requestedReceiveQuality.delete(streamId);
    else
        requestedReceiveQuality.set(streamId, quality);
    const player = activePlayers.get(streamId);
    player?.setExpectedPaused(quality !== null && quality.layerId < 0);
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
    codecSlowTickCount: number;
    decoderStats: PlayerStats | null;
    avDriftMs: number | null;
    forwarded: {
        ForwardedLayerId: number;
        ForwardedWidth: number;
        ForwardedHeight: number;
        ObservedMaxLayerId: number;
    } | null;
    requestedReceiveQuality: {
        layerId: number;
    } | null;
    streamAgeMs: number;
    // Cumulative drop-stage histogram from the player's stats. Keys are
    // decimal FrameDropStage values; only non-zero stages emitted.
    dropTraceByStage: Record<string, number>;
    // Cumulative bytes received.
    bytesReceived: number;
    // Cumulative frames presented.
    presented: number;
    // Per-tick instantaneous rates, computed at the latency-tap boundary
    // (≈ 1 Hz) where the wall-clock dt is known. Display these directly to
    // avoid the beat-frequency artifact from cross-cadence sampling.
    presentedPerSec: number;
    bytesPerSec: number;
    // Per-FrameDropStage drop rates; same provenance as presentedPerSec.
    dropTracePerSecByStage: Record<string, number>;
}

interface ViewportInfo {
    cssLongSide: number;
    devicePixelRatio: number;
    isFocused: boolean;
    hasDimensions: boolean;
}

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPlayer');

const PLAYBACK_PRIORITY_SECONDARY = 0;
const PLAYBACK_PRIORITY_PRIMARY = 1;

export class VideoPlayer {
    private blazorRef: DotNet.DotNetObject;
    private streamId: string;
    private authorId: string;
    private canvas: HTMLCanvasElement;
    private canvasOffscreenTransferred = false;
    private videoEl: HTMLVideoElement;
    private bgCanvasEl: HTMLCanvasElement;
    private renderBackend: RenderBackend;

    // Bg-blur lifecycle. The bg canvas is ALWAYS transferred to the
    // player worker — the worker picks WebGPU (dual-Kawase) or Canvas2D
    // (filter-blur) per its own probe + the mode hint sent here.
    //
    // Mode hints:
    //   • 'auto'     — default; worker probes WebGPU.
    //   • 'webgpu'   — force WebGPU; worker falls back to Canvas2D on init failure.
    //   • 'canvas2d' — force Canvas2D (cheap, no shaders).
    //   • 'off'      — skip the transfer entirely; no backdrop painted.
    // Override via `?bgBlur=webgpu|canvas2d|off` (auto is default).
    private bgBlurMode: BgBlurMode | 'off' = 'auto';
    private transferredBgCanvas: OffscreenCanvas | null = null;
    private bgCanvasInstallSent = false;
    private bgActive = false;

    // Player worker (the new pipeline pulls + decodes + renders entirely off-thread)
    private playerWorkerInstance: Worker | null = null;
    private playerWorker: (PlayerWorker & Disposable) | null = null;
    /** Resolves once `initPlayerWorker` has either finished setting up
     *  the worker or bailed out. `startPull` awaits this so the worker
     *  is guaranteed to be ready before `worker.start({...})` runs. */
    private playerReady: Promise<void> = Promise.resolve();
    /** Codec WebCodecs string the worker was started with — populated by
     *  `initPlayerWorker` once codec selection completes. */
    private selectedCodec: string | null = null;
    private selectedCodecedWidth: number | undefined;
    private selectedCodecedHeight: number | undefined;
    private codecCategory = '';

    private isPlaying = false;
    private tileEndingApplied = false;
    private visibilitySubscription: Subscription | null = null;

    /** True between `worker.start({...})` and the worker's stream-end
     *  callback. Used so `stop()` can issue `worker.stop(streamId)`
     *  without races. */
    private workerStreamActive = false;
    private connectivityHandlerOnline: { dispose(): void } | null = null;
    private connectivityHandlerConnected: { dispose(): void } | null = null;
    private traceKillRegistration: Disposable | null = null;
    private sharedSettingsRegistration: Disposable | null = null;

    private currentAttempt: {
        readonly attemptId: number;
        resolve: () => void;
        reject: (err: Error) => void;
    } | null = null;
    private restartAttempts = 0;
    private restartLoopRunning = false;
    private nextAttemptId = 0;

    // Diagnostics counters
    private renderFrameCount = 0;       // bumped from worker latency reports (frames presented)
    // Mirror of worker-side PlayerStats.presented, updated on every
    // latency-tap sample. Captured separately because `renderFrameCount`
    // is incremented once per sample (≈ 1 Hz), so it can't drive
    // per-second FPS readouts on its own.
    private presentedFrameCount = 0;
    // Per-tick instantaneous rates computed at the latency-tap boundary
    // where the wall-clock dt is known. Resampling cumulative counters at a
    // different cadence creates a beat-frequency artifact — same class of
    // bug as on the sender side; same fix applied uniformly across every
    // cumulative counter (presented, bytesReceived, per-stage drops).
    private presentedPerSec = 0;
    private bytesPerSec = 0;
    private readonly dropPerSec = new Map<number, number>();
    private lastLatencyTickMs = 0;
    private lastPresentedAtTick = 0;
    private lastBytesAtTick = 0;
    private lastChunksReceivedAtTick = 0;
    private lastFramesDecodedAtTick = 0;
    private readonly lastDropAtTick = new Map<number, number>();
    // α=0.3 matches the encoder-side EMA (video-recorder.ts) so the two
    // QC signals share half-life characteristics.
    private readonly decodeDeficitTicker = new ThroughputDeficitTicker(0.3);
    private receivedFrameCount = 0;
    private receivedKeyframeCount = 0;
    private receivedBytes = 0;
    private firstFrameReceivedTime = 0;
    // Ring buffer of (atMs, cumulativeBytes) samples for a windowed
    // IncomingByteRate. Cumulative-since-start would let an initial keyframe
    // burst dominate the rate for many seconds and the receiver-side QC peak
    // would lock the allocator into the wrong layer.
    private readonly bytesSamples: { atMs: number; bytes: number }[] = [];
    private static readonly bytesWindowMs = 3000;
    private forwardedLayerId = -1;
    private forwardedWidth = 0;
    private forwardedHeight = 0;
    private observedMaxLayerId = -1;

    // PLI: receiver-requested keyframe (kept as a courtesy hook — most of
    // this is now driven by the worker's epoch-reset operator).
    private lastKeyFrameRequestTime = 0;
    private readonly keyFrameRequestCooldownMs = 10000;

    // Render-quality hint state.
    private resizeObserver: ResizeObserver | null = null;
    // Layout swaps (item-focused ↔ item-x) change the canvas's direct
    // parent class without always producing a content-box change on the
    // canvas itself, so ResizeObserver alone misses them.
    private parentClassObserver: MutationObserver | null = null;
    // Minimize/maximize / collapse / expand are class swaps on the OUTER
    // `.video-panel` ancestor (many levels above the canvas). Watch its
    // class + size so we re-evaluate when the whole panel reshapes.
    private panelClassObserver: MutationObserver | null = null;
    private panelResizeObserver: ResizeObserver | null = null;
    private viewportCheckRafHandle: number | null = null;
    private lastSentViewportInfo: string | null = null;

    // Smoothed pipeline latency from the worker's latency-tap.
    private pipelineLatencyMs = 0;
    private skipToLiveCount = 0;
    private lastQualitySkipToLiveCount = 0;
    private readonly createdAtMs = performance.now();
    private lastArrivedOffsetMs = 0;
    private lastRenderedOffsetMs = 0;

    private codecExclusionRequested = false;

    private startedAtMs: number;

    private audioCaTimer: ReturnType<typeof setInterval> | null = null;
    private readonly videoLagEma = new RunningEMA(0, 3, 0.3);
    private readonly displayLatencyEma = new RunningEMA(0, 3, 0.3);
    private displayLatencyMs = 0;
    private hasDisplayLag = false;
    private rvfcStop = false;

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
        // Bg canvas always transfers to the worker; the worker constructs
        // the renderer per the mode hint. Default 'auto' resolves to WebGL2
        // dual-Kawase in the worker (matches the WebGPU look, runs everywhere).
        // Override via ?bgBlur=webgpu|webgl|webgl-kawase|canvas2d|off.
        const override = readBgBlurOverride();
        if (override === 'off')
            this.bgBlurMode = 'off';
        else if (override)
            this.bgBlurMode = override;
        else
            this.bgBlurMode = 'auto';
        if (this.bgBlurMode !== 'off') {
            try {
                this.transferredBgCanvas = bgCanvasEl.transferControlToOffscreen();
            } catch (e) {
                warnLog?.log('transferControlToOffscreen for bg canvas failed; backdrop disabled:', e);
                this.bgBlurMode = 'off';
                this.transferredBgCanvas = null;
            }
        }
        infoLog?.log(`Bg-blur mode: ${this.bgBlurMode}${override ? ` (override=${override})` : ''}`);
        this.renderBackend = pickRenderBackend(canvas, videoEl);
        this.applyBackendVisibility(canvas, videoEl);

        // Set canvas size
        canvas.width = width || 1280;
        canvas.height = height || 720;

        debugLog?.log(
            `VideoPlayer created for stream ${streamId}, codec: ${codec}, size: ${width}x${height}, ` +
            `authorId=${authorId}, startedAtMs=${startedAtMs.toFixed(0)}`);

        // A/V-sync: push the audio capture-point to the worker so a video skip
        // lands on the audio timeline instead of the live edge. Cheap no-op
        // until a worker stream is active and a fresh audio latency exists.
        this.audioCaTimer = setInterval(() => this.pushAudioCaptureOffset(), 150);

        // Diagnostics: measure display/compositor latency (present → on-screen)
        // via rVFC, the symmetric counterpart of audio's AudioContext output latency.
        this.startDisplayLatencyTracking();

        activePlayers.set(streamId, this);
        infoLog?.log(`VideoPlayer registry: added ${streamId}, active=${activePlayers.size}`);

        this.playerReady = this.initPlayerWorker(codec, width, height, codecSettings);
    }

    // Fade the tile out the moment we know the stream has ended gracefully,
    // so the user doesn't see the last decoded frame frozen during the
    // Blazor invalidate → recompute → unmount → DisposeAsync → stop() chain.
    private markTileEnding(reason: string): void {
        if (this.tileEndingApplied) return;
        this.tileEndingApplied = true;
        const tile = this.canvas.parentElement;
        if (!tile) return;
        tile.classList.add('is-ending');
        debugLog?.log(`markTileEnding: ${reason}`);
    }

    private applyBackendVisibility(canvas: HTMLCanvasElement, videoEl: HTMLVideoElement): void {
        if (this.renderBackend.kind === 'mstg') {
            videoEl.style.display = 'block';
            canvas.style.display = 'none';
        } else {
            canvas.style.display = 'block';
            videoEl.style.display = 'none';
        }
        // Re-seed the fit / backdrop on the new backend so a fallback swap
        // doesn't leave us showing cover with no painter attached.
        this.applyFitDecision();
        this.renderBackend.setExpectedPaused(this.getExpectedPaused());
    }

    private startDisplayLatencyTracking(): void {
        const videoEl = this.videoEl;
        if (typeof videoEl.requestVideoFrameCallback !== 'function')
            return;
        const onFrame = (_now: DOMHighResTimeStamp, metadata: VideoFrameCallbackMetadata): void => {
            // True capture→on-screen latency: mediaTime is the displayed frame's
            // offset from stream start (we stamp chunk.timestamp = offset), so
            // now − (anchor + mediaTime) is its age on screen — this INCLUDES the
            // MSTG→<video> playback buffer that the pre-present latency-tap misses.
            const lagMs = ServerClock.now() - (this.startedAtMs + metadata.mediaTime * 1000);
            if (Number.isFinite(lagMs) && lagMs >= 0 && lagMs < 30_000) {
                this.displayLatencyEma.appendSample(lagMs);
                this.displayLatencyMs = this.displayLatencyEma.value;
                this.hasDisplayLag = true;
            }
            if (!this.rvfcStop)
                videoEl.requestVideoFrameCallback(onFrame);
        };
        videoEl.requestVideoFrameCallback(onFrame);
    }

    private pushAudioCaptureOffset(): void {
        if (!this.workerStreamActive || !this.playerWorker)
            return;
        const audioLatencyMs = isSkipToAudioEnabled() ? getAudioLatency(this.authorId) : null;
        const caOffsetMs = audioLatencyMs === null
            ? null
            : (ServerClock.now() - audioLatencyMs) - this.startedAtMs;
        this.playerWorker.setAudioCaptureOffsetMs(this.streamId, caOffsetMs, rpcNoWait)
            .catch((e: unknown) => warnLog?.log('setAudioCaptureOffsetMs failed:', e));
    }

    setExpectedPaused(paused: boolean): void {
        try { this.renderBackend.setExpectedPaused(paused); }
        catch (e) { warnLog?.log('setExpectedPaused failed:', e); }
        // The worker's 30s no-chunk stall timer would tear down a healthy
        // paused pipeline; let it know to suspend the timer.
        if (this.playerWorker) {
            this.playerWorker.setExpectedPaused(this.streamId, paused, rpcNoWait)
                .catch((e: unknown) => warnLog?.log('worker setExpectedPaused failed:', e));
        }
    }

    private getExpectedPaused(): boolean {
        const requested = requestedReceiveQuality.get(this.streamId);
        return requested !== undefined && requested !== null && requested.layerId < 0;
    }

    private async initPlayerWorker(codec: string, width: number, height: number, codecSettings: string): Promise<void> {
        if (!this.supportsWebCodecs()) {
            warnLog?.log('WebCodecs not supported');
            return;
        }

        try {
            // Decode codec settings (base64 encoded SPS/PPS for H.264/HEVC)
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

            const dims = (width && height) ? { width, height } : undefined;
            const selection = await selectDecoderCodec(candidates, description, dims);
            if (!selection) {
                warnLog?.log(`No HW-supported codec found among candidates: [${candidates.join(', ')}]`);
                this.isPlaying = false;
                void this.reportEnded(`Codec not supported`);
                return;
            }
            const codecString = selection.codec;
            this.selectedCodec = codecString;
            this.selectedCodecedWidth = width || undefined;
            this.selectedCodecedHeight = height || undefined;
            this.codecCategory = VideoPlayer.getCodecCategory(codecString);
            debugLog?.log(`Selected decoder codec: ${codecString} (accel: ${selection.hardwareAcceleration})`);

            // Construct the new player worker bundle.
            const playerWorkerPath = Versioning.mapPath('/dist/videoPlayerWorker.js');
            this.playerWorkerInstance = new Worker(playerWorkerPath, { type: 'module' });
            this.playerWorkerInstance.onerror = (e) => errorLog?.log('Player worker error:', e);

            this.playerWorker = rpcClientServer<PlayerWorker>(
                'VideoPlayer.player',
                this.playerWorkerInstance,
                {
                    getSessionToken: (minLifespanMs?: number) => Api.getSessionToken(minLifespanMs),
                    onTrackReady: (streamId: string, kind: RenderBackendKind, track: MediaStreamTrack | null) => {
                        debugLog?.log(`onTrackReady: stream=${streamId}, backend=${kind}, track=${track ? 'yes' : 'null'}`);
                        if (kind === 'mstg' && track && this.renderBackend.kind === 'mstg') {
                            // Worker-built MSTG/VTG (Tier 1): hand the track to
                            // the off-thread backend so the watchdog, resize
                            // listener, and play() retry are wired up the same
                            // way as the main-thread (Tier 2) path.
                            (this.renderBackend as OffThreadRenderBackend).onTrackReady(track);
                        }
                        return Promise.resolve();
                    },
                    onLatencyReport: (streamId: string, sample: LatencySample) => {
                        this.onWorkerLatencyReport(streamId, sample);
                        return Promise.resolve();
                    },
                    onStreamEnded: (streamId: string, reason: string) => {
                        debugLog?.log(`Worker stream ended: stream=${streamId}, reason=${reason}`);
                        if (reason === 'completed')
                            this.settleCurrentAttempt({ kind: 'completed' });
                        else
                            this.settleCurrentAttempt({ kind: 'error', error: new Error(`stream ended: ${reason}`) });
                        return Promise.resolve();
                    },
                    onError: (streamId: string, error: string) => {
                        warnLog?.log(`Worker reported error for stream ${streamId}: ${error}`);
                        this.settleCurrentAttempt({
                            kind: 'error',
                            error: new Error(error),
                        });
                        return Promise.resolve();
                    },
                    onTraceKillInjected: () => {
                        consumeVideoTraceKill('playback');
                        return Promise.resolve();
                    },
                    onCodecProven: (streamId: string, codec: string) => {
                        const category = VideoPlayer.getCodecCategory(codec);
                        debugLog?.log(`Worker reported codec proven: stream=${streamId}, codec=${codec} → ${category}`);
                        if (category && category !== 'unknown')
                            markDecoderCodecProven(category);
                        return Promise.resolve();
                    },
                }
            );
            this.traceKillRegistration = registerVideoTraceKillWorker('playback', this.playerWorker);
            this.sharedSettingsRegistration = SharedSettingsWorkerSync.register(this.playerWorker);

            // Hand the bg-blur OffscreenCanvas to the worker as soon as the
            // RPC pair is up. Fire-and-forget — RPC message order is preserved,
            // so a setBgActive issued before install resolves still lands
            // correctly. Flush the latched bgActive state in the same .then so
            // a focused tile that was decided before the worker came online
            // immediately starts painting.
            if (this.bgBlurMode !== 'off' && this.transferredBgCanvas && !this.bgCanvasInstallSent) {
                this.bgCanvasInstallSent = true;
                const canvas = this.transferredBgCanvas;
                const mode = this.bgBlurMode;
                this.transferredBgCanvas = null; // ownership moves to worker
                void this.playerWorker.installBgCanvas(this.streamId, mode, canvas)
                    .then(() => this.playerWorker?.setBgActive(this.streamId, this.bgActive, rpcNoWait))
                    .catch((e: unknown) => warnLog?.log('installBgCanvas failed:', e));
            }

            // Seed worker-local app constants so push-to-pull-buffer / decoder
            // helpers can read VIDEO/AUDIO. AC is structurally cloneable.
            void this.playerWorker.init(AC).catch((e: unknown) => {
                warnLog?.log('Player worker init failed:', e);
            });

            // Mirror main-thread ConnectivityUI → worker connectivity.
            const pushConnectivity = (): void => {
                if (!this.playerWorker) return;
                void this.playerWorker.onConnectivityUpdate(
                    ConnectivityUI.isOnline,
                    ConnectivityUI.isConnected,
                    ConnectivityUI.isBlazorServer,
                    rpcNoWait);
            };
            this.connectivityHandlerOnline = ConnectivityUI.isOnlineChanged.add(pushConnectivity);
            this.connectivityHandlerConnected = ConnectivityUI.isConnectedChanged.add(pushConnectivity);
            void ConnectivityUI.whenReady.then(pushConnectivity);

            // Wire focused-state changes on the mstg backend. The worker no
            // longer paints the bg canvas, so the focused hook becomes a no-op
            // — kept to preserve the existing observer wiring.
            if (this.renderBackend.kind === 'mstg') {
                const mstgBackend = this.renderBackend as OffThreadRenderBackend;
                mstgBackend.onFocusedChange = (focused: boolean) => { void focused; };
                mstgBackend.onPlaybackStalled = report => {
                    this.fallbackFromMstgToCanvas(
                        `watchdog:${report.reason}, readyState=${report.readyState}, ` +
                        `videoWH=${report.videoWidth}x${report.videoHeight}, tracks=[${report.tracks}]`);
                };
            }

            // Pre-warm the worker's Fusion RPC peer so the WS handshake
            // overlaps the rest of init.
            const apiUrl = BrowserInit.getUrl('/rpc/ws').replace(/^http/, 'ws');
            void this.playerWorker.prewarmRpc(apiUrl, rpcNoWait);

            debugLog?.log('Player worker initialized');
        } catch (error) {
            errorLog?.log('Failed to initialize player worker:', error);
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

    private shouldRequestCodecExclusion(): boolean {
        return this.codecCategory !== ''
            && this.codecCategory !== 'h264'
            && this.codecCategory !== 'unknown';
    }

    /**
     * Legacy entry point — kept as a no-op so any stray Blazor / TS call
     * site that hasn't been migrated yet still compiles and runs without
     * throwing. The new pipeline pulls + decodes + renders entirely
     * inside the player worker.
     */
    public pushFrame(
        _frameData: Uint8Array,
        _timestampMs: number,
        _durationMs: number,
        _isKeyFrame: boolean,
        _description?: Uint8Array,
        _width?: number,
        _height?: number,
    ): void {
        // No-op. Frames are pulled by the worker.
    }

    public start(): void {
        if (this.isPlaying) return;

        this.isPlaying = true;
        // Per-instance scope — refcounts across concurrent players so one
        // stopping doesn't park the peer that other players still need.
        Api.requireConnection(`VideoPlayer:${this.streamId}`);
        debugLog?.log(`VideoPlayer started for stream ${this.streamId}`);

        // Listen for tab visibility restore — the new pipeline auto-recovers
        // via epoch-reset, but a PLI nudge speeds up the re-bootstrap.
        this.visibilitySubscription = DocumentEvents.passive.visibilityChange$.subscribe(() => {
            if (!document.hidden && this.isPlaying) {
                debugLog?.log('visibilityChange: tab became visible');
                this.requestKeyFrame();
            }
        });

        // Watch the tile (parent of canvas + video) for layout changes and
        // send render-size hints when the displayed long side changes enough
        // to affect playback quality. Observing the parent instead of the
        // canvas is backend-agnostic — the canvas is `display: none` on the
        // MSTG path and never reports a size change there.
        // RAF-defer so getBoundingClientRect reads post-layout dimensions.
        const parent = this.canvas.parentElement;
        this.resizeObserver = new ResizeObserver(() => this.scheduleViewportCheck());
        this.resizeObserver.observe(parent ?? this.canvas);
        // Backstop 1: layout swap on the tile (item-focused ↔ item-x).
        if (parent) {
            this.parentClassObserver = new MutationObserver(() => this.scheduleViewportCheck());
            this.parentClassObserver.observe(parent, { attributes: true, attributeFilter: ['class'] });
        }
        // Backstop 2: collapse / expand / minimize / maximize toggle classes
        // on the outer `.video-panel` ancestor, many levels above the canvas.
        // Watch both its class and its size — the panel-level reshape is what
        // ultimately drives our viewport.
        const panel = this.canvas.closest('.video-panel');
        if (panel) {
            this.panelClassObserver = new MutationObserver(() => this.scheduleViewportCheck());
            this.panelClassObserver.observe(panel, { attributes: true, attributeFilter: ['class'] });
            this.panelResizeObserver = new ResizeObserver(() => this.scheduleViewportCheck());
            this.panelResizeObserver.observe(panel);
        }
        this.maybeSendViewportChanged();

        void this.reportPlaying(0, true);
    }

    // Post-rotation frame dims, last seen on a latency report. Used by
    // applyFitDecision to recompute cover/contain on tile resize without
    // waiting for a new frame.
    private lastFrameW = 0;
    private lastFrameH = 0;
    // Loss threshold: when cover would crop >COVER_LOSS_MAX of source
    // pixels, switch to contain and paint the blurred backdrop.
    private static readonly COVER_LOSS_MAX = 0.20;

    // Cover crops the source to fill the tile; the cropped fraction equals
    // 1 − min(frameW·tileH, frameH·tileW) / max(...). When that's small
    // we keep cover (a thin sliver is invisible); when it grows past the
    // threshold we fall back to contain and light up the blurred backdrop
    // on focused tiles. Sidebar/PiP/minimized tiles are cover-only; the island
    // itself is resized to the stream aspect, so cover fills without gray bars.
    private applyFitDecision(): void {
        const backend = this.renderBackend;
        const parent = this.canvas.parentElement;
        if (!parent) return;
        const focused = parent.classList.contains('item-focused');
        const isMinimized = !!document.querySelector('.video-panel.collapsed');
        if (focused)
            this.updateCollapsedIslandAspect();
        if (!focused) {
            try { backend.setFit('cover'); } catch { /* ignore */ }
            this.applyBackdrop(false);
            return;
        }
        const fit = this.computeFocusedFit(parent);
        if (isMinimized) {
            try { backend.setFit(fit); } catch { /* ignore */ }
            this.applyBackdrop(false);
            return;
        }
        try { backend.setFit(fit); } catch (e) { warnLog?.log('setFit failed:', e); }
        this.applyBackdrop(true);
    }

    // Dispatch a backdrop on/off decision. The bg canvas lives in the
    // worker now; backends are always told setBackdrop(null) and the
    // controller-side renderer is gated by setBgActive.
    private applyBackdrop(focused: boolean): void {
        try { this.renderBackend.setBackdrop(null, false); } catch { /* ignore */ }
        if (this.bgBlurMode === 'off') return;
        this.setBgActive(focused);
    }

    private setBgActive(active: boolean): void {
        if (this.bgActive === active) return;
        this.bgActive = active;
        const worker = this.playerWorker;
        if (!worker) return; // Flushed once initPlayerWorker brings the worker up.
        void worker.setBgActive(this.streamId, active, rpcNoWait)
            .catch((e: unknown) => warnLog?.log('worker setBgActive failed:', e));
    }

    private computeFocusedFit(parent: Element): 'cover' | 'contain' {
        const rect = parent.getBoundingClientRect();
        const tileW = rect.width;
        const tileH = rect.height;
        const fw = this.lastFrameW;
        const fh = this.lastFrameH;
        if (fw <= 0 || fh <= 0 || tileW <= 0 || tileH <= 0)
            return 'cover';
        const a = fw * tileH;
        const b = fh * tileW;
        const cropLoss = 1 - Math.min(a, b) / Math.max(a, b);
        return cropLoss > VideoPlayer.COVER_LOSS_MAX ? 'contain' : 'cover';
    }

    private updateCollapsedIslandAspect(): void {
        const panel = this.canvas.closest<HTMLElement>('.video-panel');
        if (!panel) return;
        let frameW = this.lastFrameW;
        let frameH = this.lastFrameH;
        if (frameW <= 0 || frameH <= 0) {
            const ratio = readAspectRatio(this.canvas.parentElement);
            if (ratio > 0 && Number.isFinite(ratio)) {
                frameW = ratio;
                frameH = 1;
            }
        }
        const prevAspect = panel.style.getPropertyValue('--video-panel-island-aspect');
        const prevPortrait = panel.classList.contains('portrait-video');
        updateCollapsedIslandAspect(panel, frameW, frameH);
        const aspectChanged = panel.style.getPropertyValue('--video-panel-island-aspect') !== prevAspect;
        const portraitChanged = panel.classList.contains('portrait-video') !== prevPortrait;
        if (aspectChanged || portraitChanged) {
            void panel.offsetHeight;
            this.runViewportCheck();
            requestAnimationFrame(() => {
                this.runViewportCheck();
                requestAnimationFrame(() => this.runViewportCheck());
            });
        }
    }

    private scheduleViewportCheck(): void {
        // Coalesce bursts of layout/class events into a single post-layout
        // read so getBoundingClientRect lands AFTER the browser's reflow.
        if (this.viewportCheckRafHandle !== null)
            return;

        this.viewportCheckRafHandle = requestAnimationFrame(() => {
            this.viewportCheckRafHandle = null;
            this.runViewportCheck();
            // Fullscreen enter/reparent can report the focused tile's previous
            // inline size for one frame on mobile. Odd-quarter rotation bakes
            // that size into the inner canvas/video, so run one settled pass
            // after fixed-position layout has landed.
            if (this.canvas.closest('.video-panel')?.classList.contains('expanded'))
                requestAnimationFrame(() => this.runViewportCheck());
        });
    }

    private runViewportCheck(): void {
        this.maybeSendViewportChanged();
        try { this.renderBackend.recomputeLayout(); }
        catch (e) { warnLog?.log('recomputeLayout failed:', e); }
        this.applyFitDecision();
    }

    private maybeSendViewportChanged(): void {
        const info = this.computeViewportChangedInfo();
        const priority = priorityForRenderSize(info);
        const hasDimensions = info?.hasDimensions ?? false;
        const key = info
            ? `${Math.round(info.cssLongSide)}:${info.devicePixelRatio.toFixed(3)}:${priority}:${hasDimensions}`
            : `none:${priority}`;
        if (key === this.lastSentViewportInfo) return undefined;
        this.lastSentViewportInfo = key;

        debugLog?.log(
            `viewport changed: css=${info?.cssLongSide ?? 0} dpr=${info?.devicePixelRatio ?? 0} ` +
            `priority=${priority} hasDimensions=${hasDimensions} ` +
            `(canvas=${this.canvas.clientWidth}x${this.canvas.clientHeight})`);
        void this.blazorRef.invokeMethodAsync(
            'OnPlaybackViewportChanged',
            info?.cssLongSide ?? 0,
            info?.devicePixelRatio ?? 0,
            priority,
            hasDimensions)
            .catch((e: unknown) => errorLog?.log('OnPlaybackViewportChanged failed:', e));
    }

    private requestKeyFrame(): void {
        const now = performance.now();
        if (now - this.lastKeyFrameRequestTime < this.keyFrameRequestCooldownMs)
            return;
        this.lastKeyFrameRequestTime = now;

        infoLog?.log(`requestKeyFrame: stream=${this.streamId}`);
        // Worker-side hint (the new contract carries this, but the host
        // currently no-ops — kept for forward compatibility) plus the
        // direct streaming-API call so the server still gets a PLI.
        if (this.playerWorker)
            void this.playerWorker.requestKeyframe(this.streamId)
                .catch(() => { /* worker stub is a no-op; ignore */ });
        streamingApi.liveVideoStreams.RequestKeyFrame(RPC_SESSION_DEFAULT, this.streamId)
            .catch((e: unknown) => warnLog?.log('RequestKeyFrame error:', e));
    }

    public async startPull(streamId: string, skipToMs: number): Promise<void> {
        if (!this.isPlaying) {
            warnLog?.log('startPull called but player not started');
            return;
        }
        infoLog?.log(
            `startPull:streamId=${streamId}, skipToMs=${skipToMs.toFixed(0)}, ` +
            `renderBackend=${this.renderBackend.kind}, isOffThread=${this.renderBackend.isOffThread}`);

        // Wait for the player worker to finish initialization.
        await this.playerReady;

        if (!this.playerWorker || !this.selectedCodec) {
            warnLog?.log('startPull: player worker unavailable (codec selection failed?)');
            void this.reportEnded('Codec not supported');
            return;
        }

        // skipToMs is informational on the new pipeline — epoch-reset
        // handles bootstrap automatically. Recorded for diagnostics.
        void skipToMs;

        if (this.restartLoopRunning) {
            warnLog?.log(`startPull: restart loop already running for stream ${streamId}`);
            return;
        }
        this.restartLoopRunning = true;
        void this.runPlaybackLoop(streamId).finally(() => {
            this.restartLoopRunning = false;
        });
    }

    private async runPlaybackLoop(streamId: string): Promise<void> {
        this.restartAttempts = 0;
        while (this.isPlaying && !this.codecExclusionRequested) {
            try {
                await this.runOneAttempt(streamId);
                this.markTileEnding('graceful-completion');
                void this.reportEnded(undefined);
                return;
            } catch (err) {
                const e = err instanceof Error ? err : new Error(String(err));

                // Only [CODEC_EXHAUSTED] from the decode operator triggers exclusion;
                // wire stalls and other errors look codec-ish but aren't.
                if (isCodecExhaustedError(e)) {
                    const eligible = this.shouldRequestCodecExclusion()
                        && !isDecoderCodecProven(this.codecCategory);
                    if (eligible) {
                        this.codecExclusionRequested = true;
                        warnLog?.log(
                            `runPlaybackLoop: codec exhausted (${this.codecCategory}) — ` +
                            `requesting exclusion (${e.message})`);
                        void this.blazorRef.invokeMethodAsync('OnRequestCodecExclusion', this.codecCategory);
                        return;
                    }
                    if (isDecoderCodecProven(this.codecCategory))
                        infoLog?.log(
                            `runPlaybackLoop: codec ${this.codecCategory} already proven — treating as transient`);
                }

                if (!this.shouldRunPlaybackLoop())
                    return;

                this.restartAttempts++;
                const delayMs = Math.min(3000, 150 * Math.pow(1.7, this.restartAttempts - 1));
                warnLog?.log(
                    `runPlaybackLoop: attempt ${this.restartAttempts} failed — ` +
                    `${e.message}; retrying in ${delayMs.toFixed(0)}ms`);
                await delayAsync(delayMs);
            }
        }
    }

    private async runOneAttempt(streamId: string): Promise<void> {
        if (!this.playerWorker || !this.selectedCodec)
            throw new Error('runOneAttempt: worker or codec missing');

        const attemptId = this.nextAttemptId++;
        const settled = new Promise<void>((resolve, reject) => {
            this.currentAttempt = { attemptId, resolve, reject };
        });

        try {
            await this.startWorkerForAttempt(streamId);
        } catch (e) {
            if (this.currentAttempt?.attemptId === attemptId)
                this.currentAttempt = null;
            throw e;
        }

        try {
            await settled;
        } finally {
            if (this.currentAttempt?.attemptId === attemptId)
                this.currentAttempt = null;
            if (this.workerStreamActive) {
                try { await this.playerWorker.stop(streamId); }
                catch { /* ignore */ }
                this.workerStreamActive = false;
            }
        }
    }

    private shouldRunPlaybackLoop(): boolean {
        return this.isPlaying && !this.codecExclusionRequested;
    }

    private async startWorkerForAttempt(streamId: string): Promise<void> {
        if (!this.playerWorker || !this.selectedCodec)
            throw new Error('startWorkerForAttempt: worker or codec missing');

        const backend: 'mstg' | 'canvas' = this.renderBackend.isOffThread ? 'mstg' : 'canvas';
        let mstgWritable: WritableStream<VideoFrame> | undefined;
        let mstgGenerator: MediaStreamTrack | null = null;
        if (backend === 'mstg') {
            const Ctor = (globalThis as unknown as {
                MediaStreamTrackGenerator?: new (init: { kind: 'video' }) =>
                    MediaStreamTrack & { readonly writable: WritableStream<VideoFrame> };
            }).MediaStreamTrackGenerator;
            if (typeof Ctor === 'function') {
                try {
                    const gen = new Ctor({ kind: 'video' });
                    mstgGenerator = gen;
                    mstgWritable = gen.writable;
                    (this.renderBackend as OffThreadRenderBackend).onTrackReady(gen);
                    debugLog?.log(`startWorkerForAttempt: Tier 2 MSTG (id=${gen.id})`);
                } catch (e) {
                    warnLog?.log('startWorkerForAttempt: main-thread MSTG construct failed:', e);
                    mstgGenerator = null;
                    mstgWritable = undefined;
                }
            }
        }

        let offscreen: OffscreenCanvas | undefined;
        if (backend === 'canvas')
            offscreen = this.transferCanvasToOffscreen('startWorkerForAttempt');

        try {
            this.workerStreamActive = true;
            await this.playerWorker.start({
                streamId,
                initialDecoderConfig: {
                    codec: this.selectedCodec,
                    codedWidth: this.selectedCodecedWidth,
                    codedHeight: this.selectedCodecedHeight,
                },
                targetBufferSpanMs: VIDEO.targetBufferSpanMs,
                backend,
                expectedPaused: this.getExpectedPaused(),
            }, mstgWritable, offscreen);
            debugLog?.log(
                `worker.start({${streamId}}) resolved (backend=${backend}, ` +
                `tier=${mstgWritable ? '2' : (backend === 'mstg' ? '1' : 'canvas')})`);
        } catch (err) {
            this.workerStreamActive = false;
            const message = err instanceof Error ? err.message : String(err);
            // Neither main MSTG nor worker VTG/MSTG available → permanently switch to canvas.
            if (backend === 'mstg' && /MediaStreamTrackGenerator|VideoTrackGenerator|mstgWritable/.test(message)) {
                if (mstgGenerator) {
                    try { mstgGenerator.stop(); } catch { /* ignore */ }
                }
                warnLog?.log(`startWorkerForAttempt: MSTG unavailable — switching backend to canvas`);
                this.renderBackend.dispose();
                this.renderBackend = new TransferableCanvasRenderBackend(this.canvas);
                this.applyBackendVisibility(this.canvas, this.videoEl);
            }
            throw err;
        }
    }

    private settleCurrentAttempt(outcome:
        | { kind: 'completed' }
        | { kind: 'error'; error: Error }): void {
        const attempt = this.currentAttempt;
        if (!attempt)
            return;

        this.currentAttempt = null;
        if (outcome.kind === 'completed')
            attempt.resolve();
        else
            attempt.reject(outcome.error);
    }

    public stopPull(): void {
        if (!this.workerStreamActive || !this.playerWorker) return;
        this.workerStreamActive = false;
        // Drop out of the restart loop too — without this, worker.stop's locallyStopped
        // suppresses the callback, `await settled` hangs, and restartLoopRunning stays true
        // forever, blocking any future startPull.
        this.isPlaying = false;
        this.settleCurrentAttempt({ kind: 'error', error: new Error('VideoPlayer.stopPull') });
        void this.playerWorker.stop(this.streamId)
            .catch((e: unknown) => warnLog?.log('worker.stop error:', e));
    }


    public async getDiagnosticsAsync(): Promise<RemoteStreamDiagnostics> {
        let stats: PlayerStats | null = null;
        if (this.playerWorker) {
            try { stats = await this.playerWorker.getStats(this.streamId); } catch { /* ignore */ }
        }

        const bitrateKbps = Math.round(this.peekWindowedBytesPerSec() * 8 / 1000);

        const dropTraceByStage: Record<string, number> = {};
        if (stats) {
            for (const [stage, count] of stats.dropTrace) {
                if (count > 0)
                    dropTraceByStage[String(stage)] = count;
            }
        }

        const avDriftMs: number | null = null;
        const requested = requestedReceiveQuality.get(this.streamId) ?? null;
        const streamAgeMs = this.firstFrameReceivedTime > 0
            ? Math.round(performance.now() - this.firstFrameReceivedTime)
            : 0;

        return {
            streamId: this.streamId,
            authorId: this.authorId,
            codec: this.selectedCodec ?? 'unknown',
            codecCategory: this.codecCategory,
            bitrateKbps,
            pipelineLatencyMs: Math.round(this.pipelineLatencyMs),
            jitterBufferMs: VIDEO.targetBufferSpanMs,
            jitterEstimateMs: 0,
            smoothedRttMs: 0,
            rttGradientMs: 0,
            playbackRate: 1.0,
            // Encoded received-but-not-decoded depth, surfaced via PlayerStats.
            bufferSize: stats?.encodedQueueCount ?? 0,
            receivedFrameCount: this.receivedFrameCount,
            receivedKeyframeCount: this.receivedKeyframeCount,
            renderFrameCount: this.renderFrameCount,
            skipToLiveCount: this.skipToLiveCount,
            waitingForKeyframe: false,
            codecSlowTickCount: 0,
            decoderStats: stats,
            avDriftMs,
            forwarded: this.forwardedLayerId >= 0 ? {
                ForwardedLayerId: this.forwardedLayerId,
                ForwardedWidth: this.forwardedWidth,
                ForwardedHeight: this.forwardedHeight,
                ObservedMaxLayerId: this.observedMaxLayerId,
            } : null,
            requestedReceiveQuality: requested,
            streamAgeMs,
            dropTraceByStage,
            bytesReceived: this.receivedBytes,
            presented: stats?.presented ?? 0,
            presentedPerSec: this.presentedPerSec,
            bytesPerSec: this.bytesPerSec,
            dropTracePerSecByStage: Object.fromEntries(
                Array.from(this.dropPerSec.entries(), ([k, v]) => [String(k), v])),
        };
    }

    public peekPresentedPerSec(): number {
        return this.presentedPerSec;
    }

    private peekWindowedBytesPerSec(): number {
        if (this.bytesSamples.length < 2) return 0;
        const first = this.bytesSamples[0];
        const last = this.bytesSamples[this.bytesSamples.length - 1];
        const dtMs = last.atMs - first.atMs;
        if (dtMs <= 0) return 0;
        return Math.max(0, (last.bytes - first.bytes) * 1000 / dtMs);
    }

    private fallbackFromMstgToCanvas(reason: string): void {
        if (!this.isPlaying || this.renderBackend.kind !== 'mstg')
            return;

        warnLog?.log(`fallbackFromMstgToCanvas: ${reason}`);
        this.renderBackend.dispose();
        this.renderBackend = new TransferableCanvasRenderBackend(this.canvas);
        this.applyBackendVisibility(this.canvas, this.videoEl);
        this.settleCurrentAttempt({ kind: 'error', error: new Error(`mstg fallback: ${reason}`) });
    }

    private transferCanvasToOffscreen(context: string): OffscreenCanvas | undefined {
        if (this.canvasOffscreenTransferred)
            this.replaceCanvasElement();
        try {
            const offscreen = (this.canvas as unknown as { transferControlToOffscreen: () => OffscreenCanvas })
                .transferControlToOffscreen();
            this.canvasOffscreenTransferred = true;
            return offscreen;
        } catch (e) {
            warnLog?.log(`${context}: transferControlToOffscreen failed:`, e);
            return undefined;
        }
    }

    private replaceCanvasElement(): void {
        const replacement = this.canvas.cloneNode(false) as HTMLCanvasElement;
        replacement.width = this.canvas.width;
        replacement.height = this.canvas.height;
        this.canvas.replaceWith(replacement);
        this.canvas = replacement;
        this.canvasOffscreenTransferred = false;
        if (this.renderBackend.kind === 'canvas')
            this.renderBackend = new TransferableCanvasRenderBackend(this.canvas);
    }

    private onWorkerLatencyReport(streamId: string, sample: LatencySample): void {
        void streamId;
        // Push rotation to render backend — latencyTap fires immediately on
        // rotation change in addition to its 1Hz cadence, so this is the
        // sub-second presentation-transform hook.
        try { this.renderBackend.setRotation(sample.rotation); }
        catch (e) { warnLog?.log('setRotation failed:', e); }
        // Frame dims drive the cover-vs-contain decision below. Stash them
        // post-rotation (90/270 swap visual W/H) so resize-only triggers can
        // re-decide without waiting for the next latency tap.
        const swap = (sample.rotation & 1) === 1;
        this.lastFrameW = swap ? sample.height : sample.width;
        this.lastFrameH = swap ? sample.width : sample.height;
        this.applyFitDecision();

        // Smooth the e2e latency for diagnostics + Blazor health reports.
        this.pipelineLatencyMs = sample.e2eLatencyMs;
        this.lastArrivedOffsetMs = sample.e2eLatencyMs > 0 ? sample.e2eLatencyMs : this.lastArrivedOffsetMs;
        this.lastRenderedOffsetMs = Math.max(0, this.lastRenderedOffsetMs);

        // Mirror the worker's cumulative `bytesReceived` so the bitrate
        // calc in `reportPlaybackHealth` produces a real number; without
        // this `IncomingByteRate` was always 0, VideoQualityUI verdict
        // pegged at -1, and the allocator capped every stream at L0.
        this.receivedBytes = sample.bytesReceived;
        const nowMsForSample = performance.now();
        if (this.firstFrameReceivedTime === 0)
            this.firstFrameReceivedTime = nowMsForSample;
        this.bytesSamples.push({ atMs: nowMsForSample, bytes: this.receivedBytes });
        const cutoff = nowMsForSample - VideoPlayer.bytesWindowMs;
        while (this.bytesSamples.length > 1 && this.bytesSamples[0].atMs < cutoff)
            this.bytesSamples.shift();

        // Bump received counters for diagnostics (each report represents
        // ongoing flow; the new pipeline doesn't ship per-frame counters
        // to main).
        this.receivedFrameCount++;
        this.renderFrameCount++;
        this.presentedFrameCount = sample.playerStats.presented;
        if (this.restartAttempts > 0)
            this.restartAttempts = 0;

        const nowMs = performance.now();
        if (this.lastLatencyTickMs > 0) {
            const dt = nowMs - this.lastLatencyTickMs;
            if (dt > 0) {
                const scale = 1000 / dt;
                this.presentedPerSec = Math.max(0, this.presentedFrameCount - this.lastPresentedAtTick) * scale;
                this.bytesPerSec = Math.max(0, this.receivedBytes - this.lastBytesAtTick) * scale;
                const chunksDelta = Math.max(0,
                    sample.playerStats.chunksReceived - this.lastChunksReceivedAtTick);
                const framesDelta = Math.max(0,
                    sample.playerStats.framesDecoded - this.lastFramesDecodedAtTick);
                this.decodeDeficitTicker.tick(framesDelta, chunksDelta);
                this.dropPerSec.clear();
                for (const [stage, count] of sample.playerStats.dropTrace) {
                    const prev = this.lastDropAtTick.get(stage) ?? 0;
                    const rate = Math.max(0, count - prev) * scale;
                    if (rate > 0) this.dropPerSec.set(stage as number, rate);
                }
            }
        }

        this.lastLatencyTickMs = nowMs;
        this.lastPresentedAtTick = this.presentedFrameCount;
        this.lastBytesAtTick = this.receivedBytes;
        this.lastChunksReceivedAtTick = sample.playerStats.chunksReceived;
        this.lastFramesDecodedAtTick = sample.playerStats.framesDecoded;
        this.lastDropAtTick.clear();
        for (const [stage, count] of sample.playerStats.dropTrace)
            this.lastDropAtTick.set(stage as number, count);

        // Last-decoded-frame snapshot for diagnostics — what the modal's
        // "Resolution" row reads. Worker side has the data; latency-tap
        // is the existing main-thread channel.
        this.forwardedLayerId = sample.layerId;
        this.forwardedWidth = sample.width;
        this.forwardedHeight = sample.height;
        if (sample.layerId > this.observedMaxLayerId)
            this.observedMaxLayerId = sample.layerId;

        // A/V-sync video lag. The latency-tap is post-decode/pre-present, so its
        // age (tapLagMs) MISSES the MSTG→<video> playback buffer (under-reports,
        // badly on fast/loopback streams). Prefer the true on-screen lag from
        // requestVideoFrameCallback (displayLatencyMs); fall back to the tap lag
        // only when rVFC is unavailable (canvas backend / Firefox). The tap lag is
        // still forwarded as a diagnostic so the present→display gap is visible.
        const captureAtServerMs = this.startedAtMs + sample.capturedAtMs;
        const tapLagMs = Math.max(0, ServerClock.now() - captureAtServerMs);
        this.videoLagEma.appendSample(tapLagMs);
        const videoLagMs = this.hasDisplayLag ? this.displayLatencyMs : this.videoLagEma.value;
        void this.blazorRef.invokeMethodAsync(
            'OnPresentationLag', videoLagMs, sample.capturedAtMs, sample.bufferSpanMs,
            sample.playerStats.presentSkipRatio, this.videoLagEma.value)
            .catch(() => { /* ignore */ });

        // Push a playback-health snapshot for the server-side controller.
        this.reportPlaybackStats(sample);
    }

    public async stop(): Promise<void> {
        this.rvfcStop = true;
        if (this.audioCaTimer !== null) {
            clearInterval(this.audioCaTimer);
            this.audioCaTimer = null;
        }
        if (!this.isPlaying) return;

        infoLog?.log(`VideoPlayer stop() called for stream ${this.streamId}, rendered=${this.renderFrameCount} frames, received=${this.receivedFrameCount}`);

        activePlayers.delete(this.streamId);
        infoLog?.log(`VideoPlayer registry: removed ${this.streamId}, active=${activePlayers.size}`);

        this.isPlaying = false;
        // worker.stop below sets locallyStopped, which suppresses callbacks — settle here instead.
        this.settleCurrentAttempt({ kind: 'error', error: new Error('VideoPlayer.stop') });
        Api.releaseConnection(`VideoPlayer:${this.streamId}`);
        this.lastRenderedOffsetMs = 0;
        this.lastArrivedOffsetMs = 0;
        this.renderFrameCount = 0;
        this.presentedFrameCount = 0;
        this.presentedPerSec = 0;
        this.bytesPerSec = 0;
        this.dropPerSec.clear();
        this.lastLatencyTickMs = 0;
        this.lastChunksReceivedAtTick = 0;
        this.lastFramesDecodedAtTick = 0;
        this.decodeDeficitTicker.reset();
        this.lastPresentedAtTick = 0;
        this.lastBytesAtTick = 0;
        this.lastDropAtTick.clear();
        this.receivedFrameCount = 0;
        this.receivedKeyframeCount = 0;
        this.receivedBytes = 0;
        this.firstFrameReceivedTime = 0;
        this.bytesSamples.length = 0;
        this.pipelineLatencyMs = 0;

        if (this.visibilitySubscription) {
            this.visibilitySubscription.unsubscribe();
            this.visibilitySubscription = null;
        }

        if (this.resizeObserver) {
            this.resizeObserver.disconnect();
            this.resizeObserver = null;
        }

        if (this.parentClassObserver) {
            this.parentClassObserver.disconnect();
            this.parentClassObserver = null;
        }

        if (this.panelClassObserver) {
            this.panelClassObserver.disconnect();
            this.panelClassObserver = null;
        }

        if (this.panelResizeObserver) {
            this.panelResizeObserver.disconnect();
            this.panelResizeObserver = null;
        }

        if (this.viewportCheckRafHandle !== null) {
            cancelAnimationFrame(this.viewportCheckRafHandle);
            this.viewportCheckRafHandle = null;
        }

        // Stop the worker pipeline.
        if (this.workerStreamActive && this.playerWorker) {
            try { await this.playerWorker.stop(this.streamId); }
            catch { /* ignore */ }
            this.workerStreamActive = false;
        }

        if (this.connectivityHandlerOnline) {
            this.connectivityHandlerOnline.dispose();
            this.connectivityHandlerOnline = null;
        }
        if (this.connectivityHandlerConnected) {
            this.connectivityHandlerConnected.dispose();
            this.connectivityHandlerConnected = null;
        }
        if (this.traceKillRegistration) {
            this.traceKillRegistration.dispose();
            this.traceKillRegistration = null;
        }
        if (this.sharedSettingsRegistration) {
            this.sharedSettingsRegistration.dispose();
            this.sharedSettingsRegistration = null;
        }

        // Tear the worker down.
        if (this.playerWorker) {
            this.playerWorker.dispose();
            this.playerWorker = null;
        }
        if (this.playerWorkerInstance) {
            this.playerWorkerInstance.terminate();
            this.playerWorkerInstance = null;
        }

        this.renderBackend.dispose();

        debugLog?.log(`VideoPlayer stopped for stream ${this.streamId}`);
    }

    private computeViewportChangedInfo(): ViewportInfo | null {
        // Read from the tile (parent of canvas + video) — that's the real
        // layout box for both backends. Canvas is hidden on the MSTG path,
        // video is hidden on the canvas path; the parent always sizes the tile.
        const parent = this.canvas.parentElement;
        const parentRect = parent?.getBoundingClientRect();
        const canvasRect = this.canvas.getBoundingClientRect();
        const parentLongSide = parentRect ? Math.max(parentRect.width, parentRect.height) : 0;
        const canvasLongSide = Math.max(canvasRect.width, canvasRect.height);
        const clientLongSide = Math.max(this.canvas.clientWidth, this.canvas.clientHeight);
        const cssLongSide = parentLongSide > 0 ? parentLongSide
            : canvasLongSide > 0 ? canvasLongSide
                : clientLongSide > 0 ? clientLongSide
                    : 0;
        if (cssLongSide > 0) {
            return {
                cssLongSide,
                devicePixelRatio: getDevicePixelRatio(),
                isFocused: parent?.classList.contains('item-focused') ?? false,
                hasDimensions: true,
            };
        }
        if (this.canvas.isConnected && parent && isZeroSized(canvasRect) && parentRect && isZeroSized(parentRect))
            return {
                cssLongSide: 1,
                devicePixelRatio: getDevicePixelRatio(),
                isFocused: parent.classList.contains('item-focused'),
                hasDimensions: false,
            };
        if (parent?.classList.contains('pip-overlay') || parent?.classList.contains('item-x'))
            return {
                cssLongSide: 1,
                devicePixelRatio: getDevicePixelRatio(),
                isFocused: false,
                hasDimensions: true,
            };

        return null;
    }

    private reportPlaybackStats(sample: LatencySample): void {
        const bitrateKbps = Math.round(this.peekWindowedBytesPerSec() * 8 / 1000);
        const info = this.computeViewportChangedInfo();

        const stats = sample.playerStats;
        const stages = new Uint8Array(stats.dropTrace.size);
        const counts: number[] = new Array<number>(stats.dropTrace.size);
        let i = 0;
        for (const [stage, count] of stats.dropTrace) {
            stages[i] = stage;
            counts[i] = count;
            i++;
        }
        void this.blazorRef.invokeMethodAsync(
            'OnPlaybackStats',
            Math.round(bitrateKbps * 1000 / 8),
            sample.bufferSpanMs,
            priorityForRenderSize(info),
            Math.max(0, Math.round(performance.now() - this.createdAtMs)),
            info?.cssLongSide ?? 0,
            info?.devicePixelRatio ?? 0,
            info?.hasDimensions ?? false,
            this.selectedCodec ?? 'unknown',
            stages,
            counts,
            stats.presented,
            stats.playbackRateEma,
            stats.decodeRatioEma,
            stats.hangRateIn60s,
            stats.recoveryStreak,
            stats.presentSkipRatio,
            stats.bufferUnderrunRatio,
            stats.downlinkLatencyEma,
            stats.arrivalIntervalEma,
            this.decodeDeficitTicker.value)
            .catch((e: unknown) => warnLog?.log('reportPlaybackStats error:', e));
        // Also fire a stale-frame hint to the SKIP_TO_LIVE thresholds so
        // diagnostics still tick. The new pipeline's internal recovery
        // (epoch-reset + paced-encoded-buffer) replaces the old main-thread
        // SKIP_TO_LIVE machinery.
        if (sample.frameAgeMs > VIDEO.skipToLiveThresholdMs) {
            this.skipToLiveCount++;
            this.requestKeyFrame();
        }
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

function priorityForRenderSize(hint: ViewportInfo | null): number {
    // The focused tile is PRIMARY; sidebar and PiP tiles are SECONDARY even if
    // they are physically large. This keeps a screencast from competing with
    // the same author's camera for the primary downstream budget.
    return hint === null || hint.isFocused
        ? PLAYBACK_PRIORITY_PRIMARY
        : PLAYBACK_PRIORITY_SECONDARY;
}

function getDevicePixelRatio(): number {
    const dpr = Number.isFinite(window.devicePixelRatio) ? window.devicePixelRatio : 1;
    return Math.max(1, dpr);
}

function readAspectRatio(el: HTMLElement | null): number {
    const raw = el?.style.aspectRatio;
    if (!raw) return 0;
    const parts = raw.split('/').map(x => Number.parseFloat(x.trim()));
    if (parts.length === 2 && parts[0] > 0 && parts[1] > 0)
        return parts[0] / parts[1];
    const value = Number.parseFloat(raw);
    return value > 0 ? value : 0;
}

function isZeroSized(rect: DOMRect): boolean {
    return rect.width <= 0 && rect.height <= 0;
}

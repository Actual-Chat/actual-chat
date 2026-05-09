import { getLogs } from 'logging';
import { Api, streamingApi } from 'api';

const RPC_SESSION_DEFAULT = '~';
import { ServerClock } from 'clocks';
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import { DocumentEvents } from 'event-handling';
import { Versioning } from 'versioning';
import { type Subscription } from 'rxjs';
import type {
    PlayerWorker,
    LatencySample,
} from '../../Services/Video/playback/player-worker-contract';
import type { RenderBackendKind } from '../../Services/Video/playback/render-backends';
import type { VideoPlaybackStats } from '../../Services/Video/frame-envelopes';
import {
    getCodecCandidates,
    selectDecoderCodec,
} from '../../Services/Video/hevc-codec-selection';
import type { RenderBackend } from './render-backend';
import { TransferableCanvasRenderBackend } from './render-backend-canvas';
import { OffThreadRenderBackend, isOffThreadPlausible } from './render-backend-mstg';
import { BrowserInit } from '../../../UI.Blazor/Services/BrowserInit/browser-init';
import { ConnectivityUI } from '../../../UI.Blazor/Services/ConnectivityUI/connectivity-ui';
import { AC, VIDEO } from 'app-constants';

// Backend selection: prefer the off-thread renderer wherever a generator API
// (MediaStreamTrackGenerator on Chromium, VideoTrackGenerator on Safari) is
// plausibly available. The worker host probes the real APIs; if none exists,
// it rejects start() and we fall back to canvas.
// ?renderBackend=mstg|canvas overrides for diagnostics.
function pickRenderBackend(canvas: HTMLCanvasElement, videoEl: HTMLVideoElement): RenderBackend {
    let flag: string | null = null;
    try {
        flag = new URL(globalThis.location.href).searchParams.get('renderBackend');
    } catch { /* non-browser context */ }
    if (flag === 'canvas')
        return new TransferableCanvasRenderBackend(canvas);
    if (flag === 'mstg' || isOffThreadPlausible())
        return new OffThreadRenderBackend(videoEl);
    return new TransferableCanvasRenderBackend(canvas);
}

// Global registry of active VideoPlayer instances for diagnostics
const activePlayers = new Map<string, VideoPlayer>();
export function getActivePlayers(): ReadonlyMap<string, VideoPlayer> {
    return activePlayers;
}

const requestedReceiveQuality = new Map<string, {
    maxLayerId: number;
    maxTemporalLayerId: number
} | null>();

export function recordRequestedReceiveQuality(
    streamId: string,
    quality: { maxLayerId: number; maxTemporalLayerId: number } | null
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
    decoderStats: VideoPlaybackStats | null;
    avDriftMs: number | null;
    forwarded: {
        ForwardedLayerId: number;
        ForwardedWidth: number;
        ForwardedHeight: number;
        ObservedMaxLayerId: number;
    } | null;
    requestedReceiveQuality: {
        maxLayerId: number;
        maxTemporalLayerId: number;
    } | null;
    streamAgeMs: number;
}

interface PlaybackHealthSnapshot {
    incomingByteRate: number;
    bufferSpanMsEma: number;
    keyframeSkipsInWindow: number;
    decoderQueueDepthEma: number;
    currentMaxLayerId: number;
    currentMaxTemporalLayerId: number;
    priority: number;
    streamAgeMs: number;
    qualityReductionRequested: boolean;
    /** Smoothed end-to-end latency, ms — sourced from the worker's
     *  `latency-tap` operator. */
    latencyMsEma: number;
    renderCssLongSide: number;
    renderDevicePixelRatio: number;
    codec: string;
}

interface ViewportInfo {
    cssLongSide: number;
    devicePixelRatio: number;
}

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPlayer');

// Receiver-side jitter buffer span in ms. Drives the worker's
// `pacedEncodedBuffer` operator: smaller = lower latency, larger =
// more network jitter absorption. Sized to match the server-side
// `Constants.Video.TargetBufferSpan` (≈ 333ms, = 10 frames at
// 30fps); VideoQualityUI's `BufferDurationTooLowMs = TargetBuffer/3`
// (≈ 111ms) means a 100ms target landed BELOW the "too low" threshold
// → verdict pegged at -1 → Allocator capped at L0.
const TARGET_BUFFER_SPAN_MS = 333;
const DEFAULT_MAX_LAYER = 2;
const MAX_TEMPORAL_LAYER = 2147483647;
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
    private visibilitySubscription: Subscription | null = null;

    /** True between `worker.start({...})` and the worker's stream-end
     *  callback. Used so `stop()` can issue `worker.stop(streamId)`
     *  without races. */
    private workerStreamActive = false;
    private connectivityHandlerOnline: { dispose(): void } | null = null;
    private connectivityHandlerConnected: { dispose(): void } | null = null;

    // Diagnostics counters
    private renderFrameCount = 0;       // bumped from worker latency reports (frames presented)
    private receivedFrameCount = 0;
    private receivedKeyframeCount = 0;
    private receivedBytes = 0;
    private firstFrameReceivedTime = 0;
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
        this.renderBackend = pickRenderBackend(canvas, videoEl);
        this.applyBackendVisibility(canvas, videoEl);

        // Set canvas size
        canvas.width = width || 1280;
        canvas.height = height || 720;

        debugLog?.log(
            `VideoPlayer created for stream ${streamId}, codec: ${codec}, size: ${width}x${height}, ` +
            `authorId=${authorId}, startedAtMs=${startedAtMs.toFixed(0)}`);

        activePlayers.set(streamId, this);
        infoLog?.log(`VideoPlayer registry: added ${streamId}, active=${activePlayers.size}`);

        this.playerReady = this.initPlayerWorker(codec, width, height, codecSettings);
    }

    private applyBackendVisibility(canvas: HTMLCanvasElement, videoEl: HTMLVideoElement): void {
        if (this.renderBackend.kind === 'mstg') {
            videoEl.style.display = 'block';
            canvas.style.display = 'none';
        } else {
            canvas.style.display = 'block';
            videoEl.style.display = 'none';
        }
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
                        if (kind === 'mstg' && track) {
                            // Worker-built MSTG: attach the transferred track to
                            // the <video> srcObject so frames render directly.
                            try { this.videoEl.srcObject = new MediaStream([track]); }
                            catch (e) { warnLog?.log('onTrackReady: srcObject failed', e); }
                        }
                        return Promise.resolve();
                    },
                    onLatencyReport: (streamId: string, sample: LatencySample) => {
                        this.onWorkerLatencyReport(streamId, sample);
                        return Promise.resolve();
                    },
                    onError: (streamId: string, error: string) => {
                        warnLog?.log(`Worker reported error for stream ${streamId}: ${error}`);
                        if (this.shouldRequestCodecExclusion() && !this.codecExclusionRequested) {
                            this.codecExclusionRequested = true;
                            warnLog?.log(
                                `Worker error: requesting codec exclusion for ${this.codecCategory} ` +
                                `(reason: ${error})`);
                            void this.blazorRef.invokeMethodAsync('OnRequestCodecExclusion', this.codecCategory);
                        }
                        void this.reportEnded(error);
                        return Promise.resolve();
                    },
                    onStreamEnded: (streamId: string, reason: string) => {
                        debugLog?.log(`Worker stream ended: stream=${streamId}, reason=${reason}`);
                        void this.reportEnded();
                        return Promise.resolve();
                    },
                }
            );

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
                    void this.fallbackFromMstgToCanvas(
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

        // Watch the canvas for layout changes and send render-size hints when
        // the displayed long side changes enough to affect playback quality.
        // RAF-defer so getBoundingClientRect reads post-layout dimensions.
        this.resizeObserver = new ResizeObserver(() => this.scheduleViewportCheck());
        this.resizeObserver.observe(this.canvas);
        // Backstop 1: layout swap on the canvas's immediate parent
        // (item-focused ↔ item-x).
        const parent = this.canvas.parentElement;
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

    private scheduleViewportCheck(): void {
        // Coalesce bursts of layout/class events into a single post-layout
        // read so getBoundingClientRect lands AFTER the browser's reflow.
        if (this.viewportCheckRafHandle !== null)
            return;

        this.viewportCheckRafHandle = requestAnimationFrame(() => {
            this.viewportCheckRafHandle = null;
            this.maybeSendViewportChanged();
        });
    }

    private maybeSendViewportChanged(): void {
        const info = this.computeViewportChangedInfo();
        const priority = priorityForRenderSize(info);
        const key = info
            ? `${Math.round(info.cssLongSide)}:${info.devicePixelRatio.toFixed(3)}:${priority}`
            : `none:${priority}`;
        if (key === this.lastSentViewportInfo) return undefined;
        this.lastSentViewportInfo = key;

        debugLog?.log(
            `viewport changed: css=${info?.cssLongSide ?? 0} dpr=${info?.devicePixelRatio ?? 0} ` +
            `priority=${priority} (canvas=${this.canvas.clientWidth}x${this.canvas.clientHeight})`);
        void this.blazorRef.invokeMethodAsync(
            'OnPlaybackViewportChanged',
            info?.cssLongSide ?? 0,
            info?.devicePixelRatio ?? 0,
            priority)
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

    /** Called by Blazor */
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

        const backend: 'mstg' | 'canvas' = this.renderBackend.isOffThread ? 'mstg' : 'canvas';

        // Tier 2 MSTG path (Chromium): main owns the
        // MediaStreamTrackGenerator. Construct it here, attach the
        // resulting track to <video srcObject> on this thread, and
        // transfer the writable to the worker. The worker writes
        // decoded frames into the writable; the platform routes them
        // straight to <video> with no extra hops.
        //
        // If the main globalThis doesn't expose MSTG (Safari today),
        // fall through with `mstgWritable=undefined` — the worker host
        // then tries Tier 1 (worker-side MSTG/VTG). Per the contract,
        // canvas is the last-resort fallback when both tiers fail.
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
                    infoLog?.log(`startPull: Tier 2 — main-thread MSTG track attached (id=${gen.id})`);
                } catch (e) {
                    warnLog?.log('startPull: main-thread MSTG construct failed, falling back to worker tier:', e);
                    mstgGenerator = null;
                    mstgWritable = undefined;
                }
            } else {
                infoLog?.log('startPull: main-thread MSTG unavailable, deferring to worker tier');
            }
        }

        let offscreen: OffscreenCanvas | undefined;
        if (backend === 'canvas') {
            offscreen = this.transferCanvasToOffscreen('startPull');
        }

        try {
            this.workerStreamActive = true;
            await this.playerWorker.start({
                streamId,
                initialDecoderConfig: {
                    codec: this.selectedCodec,
                    codedWidth: this.selectedCodecedWidth,
                    codedHeight: this.selectedCodecedHeight,
                },
                targetBufferSpanMs: TARGET_BUFFER_SPAN_MS,
                backend,
            }, mstgWritable, offscreen);
            debugLog?.log(`Player worker.start({${streamId}}) resolved (backend=${backend}, tier=${mstgWritable ? '2' : (backend === 'mstg' ? '1' : 'canvas')})`);
        } catch (err) {
            this.workerStreamActive = false;
            const message = err instanceof Error ? err.message : String(err);
            // Worker rejected mstg (no Tier 1 / Tier 2 surface). Retry
            // with the canvas backend — this only fires on browsers
            // where neither main nor worker exposes MSTG/VTG (rare).
            if (backend === 'mstg' && /MediaStreamTrackGenerator|mstgWritable/.test(message)) {
                if (mstgGenerator) {
                    try { mstgGenerator.stop(); } catch { /* ignore */ }
                }
                warnLog?.log(`startPull: MSTG unavailable on both tiers — retrying with canvas backend`);
                const canvasOffscreen = this.transferCanvasToOffscreen('startPull retry');
                try {
                    this.workerStreamActive = true;
                    await this.playerWorker.start({
                        streamId,
                        initialDecoderConfig: {
                            codec: this.selectedCodec,
                            codedWidth: this.selectedCodecedWidth,
                            codedHeight: this.selectedCodecedHeight,
                        },
                        targetBufferSpanMs: TARGET_BUFFER_SPAN_MS,
                        backend: 'canvas',
                    }, undefined, canvasOffscreen);
                    debugLog?.log(`Player worker.start({${streamId}}) resolved (backend=canvas, retry)`);
                    return;
                } catch (err2) {
                    this.workerStreamActive = false;
                    const message2 = err2 instanceof Error ? err2.message : String(err2);
                    warnLog?.log(`startPull retry: worker.start rejected: ${message2}`);
                    void this.reportEnded(message2);
                    return;
                }
            }
            warnLog?.log(`startPull: worker.start rejected: ${message}`);
            void this.reportEnded(message);
        }
    }

    public stopPull(): void {
        // Stopping the worker pipeline is the new "stop pull". Idempotent.
        if (!this.workerStreamActive || !this.playerWorker) return;
        this.workerStreamActive = false;
        void this.playerWorker.stop(this.streamId)
            .catch((e: unknown) => warnLog?.log('worker.stop error:', e));
    }

    public async getDiagnosticsAsync(): Promise<RemoteStreamDiagnostics> {
        let stats: VideoPlaybackStats | null = null;
        if (this.playerWorker) {
            try { stats = await this.playerWorker.getStats(); } catch { /* ignore */ }
        }

        const elapsedSec = this.firstFrameReceivedTime > 0
            ? (performance.now() - this.firstFrameReceivedTime) / 1000
            : 0;
        const bitrateKbps = elapsedSec > 0
            ? Math.round(this.receivedBytes * 8 / elapsedSec / 1000)
            : 0;

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
            jitterBufferMs: TARGET_BUFFER_SPAN_MS,
            jitterEstimateMs: 0,
            smoothedRttMs: 0,
            rttGradientMs: 0,
            playbackRate: 1.0,
            // Encoded buffer depth lives inside the worker and isn't
            // exposed via VideoPlaybackStats; report 0 here.
            bufferSize: 0,
            receivedFrameCount: this.receivedFrameCount,
            receivedKeyframeCount: this.receivedKeyframeCount,
            renderFrameCount: this.renderFrameCount,
            skipToLiveCount: this.skipToLiveCount,
            waitingForKeyframe: false,
            qualityReductionRequested: false,
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
        };
    }

    private async fallbackFromMstgToCanvas(reason: string): Promise<void> {
        if (!this.isPlaying || this.renderBackend.kind !== 'mstg')
            return;

        warnLog?.log(`fallbackFromMstgToCanvas: ${reason}`);
        try {
            if (this.workerStreamActive && this.playerWorker) {
                try { await this.playerWorker.stop(this.streamId); }
                catch (e) { warnLog?.log('fallbackFromMstgToCanvas: worker.stop failed:', e); }
                this.workerStreamActive = false;
            }

            this.renderBackend.dispose();
            this.renderBackend = new TransferableCanvasRenderBackend(this.canvas);
            this.applyBackendVisibility(this.canvas, this.videoEl);

            const liveOffsetMs = Math.max(0, ServerClock.now() - this.startedAtMs);
            await this.startPull(this.streamId, liveOffsetMs);
        } catch (e) {
            warnLog?.log('fallbackFromMstgToCanvas: failed', e);
        }
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
        // Smooth the e2e latency for diagnostics + Blazor health reports.
        this.pipelineLatencyMs = sample.e2eLatencyMs;
        this.lastArrivedOffsetMs = sample.e2eLatencyMs > 0 ? sample.e2eLatencyMs : this.lastArrivedOffsetMs;
        this.lastRenderedOffsetMs = Math.max(0, this.lastRenderedOffsetMs);
        // Mirror the worker's cumulative `bytesReceived` so the bitrate
        // calc in `reportPlaybackHealth` produces a real number; without
        // this `IncomingByteRate` was always 0, VideoQualityUI verdict
        // pegged at -1, and the allocator capped every stream at L0.
        this.receivedBytes = sample.bytesReceived;
        if (this.firstFrameReceivedTime === 0)
            this.firstFrameReceivedTime = performance.now();
        // Bump received counters for diagnostics (each report represents
        // ongoing flow; the new pipeline doesn't ship per-frame counters
        // to main).
        this.receivedFrameCount++;
        this.renderFrameCount++;

        // Last-decoded-frame snapshot for diagnostics — what the modal's
        // "Resolution" row reads. Worker side has the data; latency-tap
        // is the existing main-thread channel.
        this.forwardedLayerId = sample.layerId;
        this.forwardedWidth = sample.width;
        this.forwardedHeight = sample.height;
        if (sample.layerId > this.observedMaxLayerId)
            this.observedMaxLayerId = sample.layerId;

        // Forward presentation lag to Blazor for A/V sync.
        const presentationLagMs = Math.max(0, sample.frameAgeMs);
        void this.blazorRef.invokeMethodAsync('OnPresentationLag', presentationLagMs)
            .catch(() => { /* ignore */ });

        // Push a playback-health snapshot for the server-side controller.
        this.reportPlaybackHealth(sample);
    }

    public async stop(): Promise<void> {
        if (!this.isPlaying) return;

        infoLog?.log(`VideoPlayer stop() called for stream ${this.streamId}, rendered=${this.renderFrameCount} frames, received=${this.receivedFrameCount}`);

        activePlayers.delete(this.streamId);
        infoLog?.log(`VideoPlayer registry: removed ${this.streamId}, active=${activePlayers.size}`);

        this.isPlaying = false;
        Api.releaseConnection(`VideoPlayer:${this.streamId}`);
        this.lastRenderedOffsetMs = 0;
        this.lastArrivedOffsetMs = 0;
        this.renderFrameCount = 0;
        this.receivedFrameCount = 0;
        this.receivedKeyframeCount = 0;
        this.receivedBytes = 0;
        this.firstFrameReceivedTime = 0;
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
        const parent = this.canvas.parentElement;
        const canvasRect = this.canvas.getBoundingClientRect();
        const parentRect = parent?.getBoundingClientRect();
        const canvasLongSide = Math.max(canvasRect.width, canvasRect.height);
        const parentLongSide = parentRect ? Math.max(parentRect.width, parentRect.height) : 0;
        const clientLongSide = Math.max(this.canvas.clientWidth, this.canvas.clientHeight);
        const cssLongSide = canvasLongSide > 0 ? canvasLongSide
            : clientLongSide > 0 ? clientLongSide
                : parentLongSide > 0 ? parentLongSide
                    : 0;
        if (cssLongSide > 0) {
            return {
                cssLongSide,
                devicePixelRatio: getDevicePixelRatio(),
            };
        }
        if (this.canvas.isConnected && parent && isZeroSized(canvasRect) && parentRect && isZeroSized(parentRect))
            return { cssLongSide: 1, devicePixelRatio: getDevicePixelRatio() };
        if (parent?.classList.contains('pip-overlay') || parent?.classList.contains('item-x'))
            return { cssLongSide: 1, devicePixelRatio: getDevicePixelRatio() };

        return null;
    }

    private reportPlaybackHealth(sample: LatencySample): void {
        const elapsedSec = this.firstFrameReceivedTime > 0
            ? (performance.now() - this.firstFrameReceivedTime) / 1000
            : 0;
        const bitrateKbps = elapsedSec > 0
            ? Math.round(this.receivedBytes * 8 / elapsedSec / 1000)
            : 0;
        const info = this.computeViewportChangedInfo();
        const requested = requestedReceiveQuality.get(this.streamId) ?? null;
        const skipDelta = Math.max(0, this.skipToLiveCount - this.lastQualitySkipToLiveCount);
        this.lastQualitySkipToLiveCount = this.skipToLiveCount;

        const snapshot: PlaybackHealthSnapshot = {
            incomingByteRate: Math.round(bitrateKbps * 1000 / 8),
            bufferSpanMsEma: sample.bufferSpanMs,
            keyframeSkipsInWindow: skipDelta,
            decoderQueueDepthEma: 0,
            currentMaxLayerId: requested?.maxLayerId ?? DEFAULT_MAX_LAYER,
            currentMaxTemporalLayerId: MAX_TEMPORAL_LAYER,
            priority: priorityForRenderSize(info),
            streamAgeMs: Math.max(0, Math.round(performance.now() - this.createdAtMs)),
            qualityReductionRequested: false,
            latencyMsEma: Math.max(0, sample.e2eLatencyMs),
            renderCssLongSide: info?.cssLongSide ?? 0,
            renderDevicePixelRatio: info?.devicePixelRatio ?? 0,
            codec: this.selectedCodec ?? 'unknown',
        };
        void this.blazorRef.invokeMethodAsync('OnPlaybackHealth', snapshot)
            .catch((e: unknown) => warnLog?.log('reportPlaybackHealth error:', e));
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
    // Anything medium-sized or larger is PRIMARY. SECONDARY is reserved for
    // tiny sidebar / pip tiles; the allocator may still give them more than L0
    // if primaries leave enough budget.
    return hint === null || hint.cssLongSide > 480
        ? PLAYBACK_PRIORITY_PRIMARY
        : PLAYBACK_PRIORITY_SECONDARY;
}

function getDevicePixelRatio(): number {
    const dpr = Number.isFinite(window.devicePixelRatio) ? window.devicePixelRatio : 1;
    return Math.max(1, dpr);
}

function isZeroSized(rect: DOMRect): boolean {
    return rect.width <= 0 && rect.height <= 0;
}

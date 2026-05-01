import { getLogs } from 'logging';
import { Api, momentToSeconds, secondsToMoment, streamingApi, type VideoFrameDto, type VideoLatencyReportResponseDto } from 'api';
import { ServerClock } from 'server-clock';
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import { AudioVideoSync } from 'audio-video-sync';
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
    forwarded: VideoLatencyReportResponseDto | null;
}

const { debugLog, warnLog, errorLog } = getLogs('VideoPlayer');

// Graduated recovery thresholds — escalating response to growing latency.
// (SKIP_TO_LIVE / LATENCY_REPORT / SLOW_DECODE thresholds now read from VIDEO.*)
const CATCHUP_GENTLE_MS = 300;        // Start gentle 1.05x catch-up
const CATCHUP_AGGRESSIVE_MS = 1000;   // Increase to 1.15x catch-up
const DROP_TO_KEYFRAME_MS = 2000;     // Drop non-keyframes from buffer, advance to next keyframe

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
    displayWidth: number;
    displayHeight: number;
    close(): void;
}

/**
 * Decoded-frame holder shaped like Denque so existing call sites work
 * unchanged. Capacity is fixed at 1: `push` closes any prior pending
 * frame before storing the new one (replaceable slot per
 * docs/video-pipeline.md). The encoded pre-decode buffer in
 * decoder-worker.ts now owns playback latency, so multi-frame catch-up
 * / hard-seek / playbackRate-chase paths in onRenderFrame become
 * no-ops (length is always 0 or 1) — left in place for now; a
 * follow-up commit removes that dead machinery.
 */
class SingleSlot<T extends { close(): void }> {
    private slot: T | null = null;
    get length(): number { return this.slot ? 1 : 0; }
    isEmpty(): boolean { return this.slot === null; }
    push(item: T): void {
        if (this.slot) {
            try { this.slot.close(); } catch { /* already closed */ }
        }
        this.slot = item;
    }
    shift(): T | undefined {
        const v = this.slot ?? undefined;
        this.slot = null;
        return v;
    }
    peekFront(): T | undefined { return this.slot ?? undefined; }
    peekBack(): T | undefined { return this.slot ?? undefined; }
    peekAt(index: number): T | undefined {
        return index === 0 ? (this.slot ?? undefined) : undefined;
    }
}

// Extract an owned ArrayBuffer from a Uint8Array. msgpack-decoded byte fields
// may be either fully-owned (whole buffer = view) or shared subarrays into a
// larger decode buffer. Fast path: when the view spans the whole underlying
// buffer, return it directly — zero alloc, zero copy. Otherwise slice() to get
// an owned copy. The returned buffer is safe to detach (transfer across worker
// boundary or pass into `new EncodedVideoChunk({ transfer: [...] })`).
// Diagnostic counters: track how often the fast path (zero-alloc) fires vs the
// slow path (slice). Logged every OWNED_ARRAY_BUFFER_LOG_INTERVAL invocations.
// If slow dominates, msgpack returns shared subarrays and a buffer-pool tweak
// might be worth it; if fast dominates, Phase A is sufficient on this hop.
let ownedArrayBufferFastCount = 0;
let ownedArrayBufferSlowCount = 0;
const OWNED_ARRAY_BUFFER_LOG_INTERVAL = 300;
function ownedArrayBuffer(view: Uint8Array): ArrayBuffer {
    const isOwned = view.byteOffset === 0 && view.byteLength === view.buffer.byteLength;
    if (isOwned) {
        ownedArrayBufferFastCount++;
    } else {
        ownedArrayBufferSlowCount++;
    }
    const total = ownedArrayBufferFastCount + ownedArrayBufferSlowCount;
    if (total % OWNED_ARRAY_BUFFER_LOG_INTERVAL === 0) {
        const fastPct = (ownedArrayBufferFastCount / total * 100).toFixed(1);
        warnLog?.log(`ownedArrayBuffer: fast=${ownedArrayBufferFastCount} ` +
            `slow=${ownedArrayBufferSlowCount} (${fastPct}% fast)`);
    }
    if (isOwned) {
        // msgpack-decoded byte fields are always plain ArrayBuffer (never
        // SharedArrayBuffer); cast narrows the ArrayBufferLike union.
        return view.buffer as ArrayBuffer;
    }
    return view.slice().buffer;
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
    // Single decoded-frame slot. The encoded pre-decode buffer (in
    // decoder-worker.ts) owns playback latency now; the canvas presentation
    // path only needs the most-recent decoded frame. SingleSlot exposes a
    // Denque-like API so the existing onRenderFrame / catchup code paths
    // keep compiling — they reduce to no-ops because length is always 0 or 1.
    private pendingFrames = new SingleSlot<PendingFrame>();
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

    // Buffering state
    private bufferSize = 0;
    private readonly maxBufferSize = 20; // frames
    private lastSoftCatchupLogTime = 0;
    private lastReportedBufferLow = true;

    // Video pull — Fusion RPC with abort controller for cancellation
    private pullAbortController: AbortController | null = null;
    private pullRetryCount = 0;
    private pullRetryTimer: ReturnType<typeof setTimeout> | null = null;

    // Off-thread mode: when true, the decoder worker owns the Fusion RPC pull
    // and main does no per-frame work. Set after a successful startPullInWorker.
    private offThreadPullActive = false;
    private offThreadSyncChannel: MessageChannel | null = null;
    // DIAG: counts entries to delegatePullToWorker for this VideoPlayer instance.
    // Used to confirm whether retry paths re-enter the off-thread setup.
    private delegateEntryCount = 0;
    private connectivityHandlerOnline: EventHandler<boolean> | null = null;
    private connectivityHandlerConnected: EventHandler<boolean> | null = null;

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
    // Last server-reported forwarded layer (response of ReportVideoLatency).
    // Surfaced to the diagnostics modal so it can show the actual delivered
    // simulcast layer + its coded WxH for THIS peer.
    private lastForwarded: VideoLatencyReportResponseDto | null = null;

    // Diagnostics counters for 10s delta reporting
    private lastDiagDecodedFrames = 0;
    private lastDiagReceivedFrames = 0;

    // Latency measurement
    private lastRenderedOffsetMs = 0;   // offset of the latest decoded frame (ms from stream start)
    private lastLatencyReportTime = 0;
    private pipelineLatencyMs = 0;      // Smoothed video pipeline latency estimate (ms)
    private lastSkipToLiveTime = 0;     // Cooldown: prevent rapid SKIP_TO_LIVE cascading
    private skipFramesBelowOffsetMs = 0; // After tab restore, skip decoded frames below this offset
    private skippedBacklogFrames = 0;
    private rebufferDelayMs = 0;         // After tab restore, delay rendering to let buffer accumulate
    private consecutiveEmptyRenders = 0; // Safety net: count consecutive RAFs with no frame rendered
    private lastHighLatencyLogTime = 0;  // Throttle high-latency FRAME_RECV logs
    private skipToLiveCount = 0;          // Number of skip-to-live events
    // Offset of the newest frame that arrived at this receiver. Used for server
    // latency reporting so the signal reflects pure network+relay transit —
    // NOT pipelineLatencyMs (the intentional jitter buffer). Reporting
    // lastRenderedOffsetMs would conflate the buffer with congestion and make
    // the server step down quality on a perfectly healthy local link.
    private lastArrivedOffsetMs = 0;

    // Adaptive jitter buffer — absorbs network jitter by delaying rendering
    private jitterBufferMs = 40;                   // Current target delay (ms)
    private readonly minJitterBufferMs = 20;
    private readonly maxJitterBufferMs = 120;
    private jitterEstimateMs = 0;                  // Smoothed inter-frame arrival jitter
    private lastFrameArrivalTime = 0;              // For jitter measurement

    // RTT measurement for proactive congestion detection
    private smoothedRttMs = 0;
    private previousRttMs = 0;
    private rttGradientMs = 0;
    private lastFrameArrivalInterval = 0;          // Previous inter-frame interval

    // Adaptive catch-up playback state (wall-clock path only)
    private playbackRate = 1.0;
    private readonly catchUpStartMs = 300;       // start speed-up when buffer > 300ms
    private readonly catchUpTargetMs = 150;       // target buffer level to settle at
    private readonly maxPlaybackRate = 1.15;      // max speed (barely noticeable for video)
    private readonly seekThresholdMs = 5000;      // hard seek fallback when >5s behind
    private lastSeekTime = 0;                     // cooldown for hard seek
    private readonly seekCooldownMs = 5000;       // min interval between hard seeks

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
            warnLog?.log('Safari detected — will convert VideoFrame to ImageBitmap for canvas rendering');

        // Set canvas size
        canvas.width = width || 1280;
        canvas.height = height || 720;

        debugLog?.log(
            `VideoPlayer created for stream ${streamId}, codec: ${codec}, size: ${width}x${height}, ` +
            `authorId=${authorId}, startedAtMs=${startedAtMs.toFixed(0)}`);

        // Register in global diagnostics registry
        activePlayers.set(streamId, this);
        warnLog?.log(`VideoPlayer registry: added ${streamId}, active=${activePlayers.size}`);

        // Initialize decoder worker — store the promise so startPull can gate
        // on it (prevents pre-init frame drop on the main-thread RPC fallback).
        this.decoderReady = this.initDecoderWorker(codec, width, height, codecSettings);
    }

    // Rule 3 — adaptive `<video>.playbackRate` to converge on the audio
    // clock. Worker exposes a smoothed signed drift (audio target − wallclock
    // target). Hysteresis: nudge to 1.05/0.95 when |drift| > 100 ms, snap back
    // to 1.0 when |drift| < 30 ms. ±5 % is below the just-noticeable
    // pitch-shift threshold for video and large enough to converge ~100 ms of
    // drift in 2 s. Runs on the watchdog cadence (~2 s) — we don't need finer.
    //
    // Why thresholds widened from 50/20 to 100/30: the worker selector now
    // tracks the full audio-pipeline lag (no ±100 ms clamp on correctionUs),
    // so steady-state drift sits at ±20–50 ms naturally. Reserving rate
    // correction for >100 ms avoids constant micro-flips during normal
    // operation; it kicks in only on the cold-audio jump (~600 ms) where
    // we genuinely need to reel video back over a few seconds.
    private async adjustPlaybackRateForDrift(): Promise<void> {
        if (!this.decoderWorker || this.renderBackend.kind !== 'mstg') return;
        let driftMs: number;
        try { driftMs = await this.decoderWorker.getDriftMs(); }
        catch { return; }
        if (!Number.isFinite(driftMs)) return;
        const current = this.videoEl.playbackRate;
        const ENTER = 100;  // start correcting when |drift| > 100 ms
        const EXIT = 30;    // snap back to 1.0 when |drift| < 30 ms
        let next = current;
        if (driftMs > ENTER) next = 1.05;             // audio ahead → speed video up
        else if (driftMs < -ENTER) next = 0.95;       // video ahead → slow video down
        else if (Math.abs(driftMs) < EXIT) next = 1.0;
        if (next !== current)
            this.videoEl.playbackRate = next;
        // Continuous A/V sync DIAG line — fires every watchdog tick (~2 s),
        // not only on rate changes. Lets us see steady-state drift without
        // requiring a rate change to surface it. `currentTime` is the visible
        // playback head (off-thread mode). `playbackRate` shows whether
        // Rule 3 is currently correcting; `→Y` part appears only on flips.
        const rateStr = next !== current
            ? `playbackRate=${current.toFixed(2)}→${next.toFixed(2)}`
            : `playbackRate=${current.toFixed(2)}`;
        warnLog?.log(
            `AVSync DIAG: driftMs=${driftMs.toFixed(0)} ` +
            `videoCurrentTime=${this.videoEl.currentTime.toFixed(3)}s ${rateStr}`);
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
            void this.decoderWorker.init(AC);

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
                // Rule 3: poll worker drift each watchdog tick (~2 s) and
                // adjust <video>.playbackRate to converge audio + video.
                mstgBackend.onWatchdogTick = () => { void this.adjustPlaybackRateForDrift(); };
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

    private wrapFrame(frame: VideoFrame): PendingFrame {
        return {
            drawable: frame,
            timestamp: frame.timestamp,
            displayWidth: frame.displayWidth,
            displayHeight: frame.displayHeight,
            close() { frame.close(); },
        };
    }

    private async convertToBitmap(frame: VideoFrame): Promise<PendingFrame> {
        const ts = frame.timestamp;
        const dw = frame.displayWidth;
        const dh = frame.displayHeight;
        try {
            const bitmap = await createImageBitmap(frame);
            frame.close();
            return {
                drawable: bitmap,
                timestamp: ts,
                displayWidth: dw,
                displayHeight: dh,
                close() { bitmap.close(); },
            };
        } catch (e) {
            warnLog?.log('createImageBitmap(VideoFrame) failed, falling back to direct frame:', e);
            return {
                drawable: frame,
                timestamp: ts,
                displayWidth: dw,
                displayHeight: dh,
                close() { frame.close(); },
            };
        }
    }

    private enqueuePendingFrame(pf: PendingFrame): void {
        // Measure inter-frame arrival jitter for adaptive jitter buffer
        const arrivalTime = performance.now();
        if (this.lastFrameArrivalTime > 0) {
            const interval = arrivalTime - this.lastFrameArrivalTime;
            if (this.lastFrameArrivalInterval > 0) {
                const jitter = Math.abs(interval - this.lastFrameArrivalInterval);
                // Exponential moving average, α=0.1 for stability
                this.jitterEstimateMs = 0.9 * this.jitterEstimateMs + 0.1 * jitter;
                // Adapt buffer: target = 2× estimated jitter, clamped
                this.jitterBufferMs = Math.max(this.minJitterBufferMs,
                    Math.min(this.maxJitterBufferMs, this.jitterEstimateMs * 2));
            }
            this.lastFrameArrivalInterval = interval;
        }
        this.lastFrameArrivalTime = arrivalTime;

        this.pendingFrames.push(pf);
        this.bufferSize++;
        this.wakeRenderLoop();

        // Update pipeline latency estimate from this fresh frame
        const frameOffsetMs = pf.timestamp / 1000; // μs → ms
        const capturedAtMs = this.startedAtMs + frameOffsetMs;
        const currentLatencyMs = ServerClock.now() - capturedAtMs;
        // Safety cap at 10s to prevent absurd values from clock drift.
        const cappedLatencyMs = Math.min(Math.max(currentLatencyMs, 0), 10000);
        if (this.pipelineLatencyMs === 0) {
            this.pipelineLatencyMs = cappedLatencyMs;
        } else {
            // Asymmetric EMA: moderate response to increases (α=0.2), faster decay (α=0.15)
            // to prevent ratchet effect where bursty delivery inflates the estimate permanently
            const alpha = cappedLatencyMs > this.pipelineLatencyMs ? 0.2 : 0.15;
            this.pipelineLatencyMs = this.pipelineLatencyMs * (1 - alpha) + cappedLatencyMs * alpha;
        }

        // Soft catchup: when buffer is significantly backed up, drop oldest frames
        // to keep only the most recent ~300ms. Normal steady-state buffer span is ~330ms
        // at 30fps, so only trigger when well above that (600ms = nearly double normal).
        if (this.pendingFrames.length > 15) {
            const bufferSpanMs = this.pendingFrames.length >= 2
                ? (this.pendingFrames.peekBack()!.timestamp - this.pendingFrames.peekFront()!.timestamp) / 1000
                : 0;
            if (bufferSpanMs > 600) {
                const targetSpanUs = 300 * 1000; // keep ~300ms worth of frames
                const cutoffTimestamp = this.pendingFrames.peekBack()!.timestamp - targetSpanUs;
                let dropCount = 0;
                while (this.pendingFrames.length > 1 && this.pendingFrames.peekFront()!.timestamp < cutoffTimestamp) {
                    this.pendingFrames.shift()!.close();
                    this.bufferSize--;
                    dropCount++;
                }
                if (dropCount > 0) {
                    const now = performance.now();
                    if (now - this.lastSoftCatchupLogTime > 1000) {
                        this.lastSoftCatchupLogTime = now;
                        debugLog?.log(`Soft catchup: dropped ${dropCount} frames, bufferSpanMs was ${bufferSpanMs.toFixed(0)}`);
                    }
                }
            }
        }

        // Hard cap: drop oldest frames if buffer still exceeds max.
        while (this.pendingFrames.length > this.maxBufferSize) {
            const dropped = this.pendingFrames.shift()!;
            dropped.close();
            this.bufferSize--;
        }
    }

    private onFrameDecoded(frame: VideoFrame): void {
        // After tab restore, skip old frames from decoder's internal backlog
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
                const pf = await this.convertToBitmap(frame);
                // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
                if (!this.isPlaying) { pf.close(); return; }
                this.enqueuePendingFrame(pf);
            });
        } else {
            this.enqueuePendingFrame(this.wrapFrame(frame));
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

        // Compute target — audio-driven when available, wall-clock fallback
        let targetTimestamp: number;
        const audioState = AudioVideoSync.get(this.authorId);
        if (audioState) {
            const audioPlayingAtMs = AudioVideoSync.interpolatePlayingAt(audioState) * 1000;
            const audioStartAtMs = audioState.recordedAtMs - audioState.playingAtSec * 1000;
            const rawTargetVideoOffsetMs = (audioStartAtMs - this.startedAtMs) + audioPlayingAtMs;
            // Audio sync already accounts for end-to-end latency through audioState.recordedAtMs —
            // subtracting pipelineLatencyMs would double-count, making the target too conservative
            // and causing buffer bloat → render stall → SKIP_TO_LIVE spiral.
            const targetVideoOffsetMs = rawTargetVideoOffsetMs;
            targetTimestamp = targetVideoOffsetMs * 1000;

            // When audio sync targets a time before this video stream started
            // (e.g., new stream created after codec switch), or far behind the
            // buffered frames (stale audio state after SKIP_TO_LIVE), snap to
            // live edge to avoid permanent render starvation.
            if (this.pendingFrames.length > 0) {
                const oldestBufferedMs = this.pendingFrames.peekFront()!.timestamp / 1000;
                if (rawTargetVideoOffsetMs < 0 || targetVideoOffsetMs < oldestBufferedMs - 2000) {
                    targetTimestamp = this.pendingFrames.peekBack()!.timestamp;
                }
            }

            this.playbackStartTime = now;
            this.firstFrameTimestamp = targetTimestamp;

            // Safety cap: flush old frames if buffer span exceeds 2s even in audio-sync mode.
            // This prevents buffer bloat from bursty delivery causing unbounded latency growth.
            if (this.pendingFrames.length >= 2) {
                const bufferSpanMs = (this.pendingFrames.peekBack()!.timestamp
                    - this.pendingFrames.peekFront()!.timestamp) / 1000;
                if (bufferSpanMs > 2000) {
                    // Find the frame closest to target and drop everything before it
                    let flushIdx = 0;
                    for (let i = 0; i < this.pendingFrames.length; i++) {
                        if (this.pendingFrames.peekAt(i)!.timestamp <= targetTimestamp) {
                            flushIdx = i;
                        } else {
                            break;
                        }
                    }
                    if (flushIdx > 0) {
                        for (let i = 0; i < flushIdx; i++) {
                            this.pendingFrames.shift()!.close();
                            this.bufferSize--;
                        }
                        warnLog?.log(
                            `audioSync buffer flush: dropped ${flushIdx} frames, ` +
                            `bufferSpanMs=${bufferSpanMs.toFixed(0)}, remaining=${this.pendingFrames.length}`);
                    }
                }
            }

            if (now - this.lastSyncLogTime > 1000) {
                this.lastSyncLogTime = now;
                const driftMs = this.lastRenderedOffsetMs - targetVideoOffsetMs;
                debugLog?.log(
                    `audioSync: rawTargetMs=${rawTargetVideoOffsetMs.toFixed(0)}, ` +
                    `pipelineMs=${this.pipelineLatencyMs.toFixed(0)}, ` +
                    `targetMs=${targetVideoOffsetMs.toFixed(0)}, ` +
                    `renderedMs=${this.lastRenderedOffsetMs.toFixed(0)}, ` +
                    `driftMs=${driftMs.toFixed(0)}, pending=${this.pendingFrames.length}`);
            }
        } else {
            // Adaptive catch-up: measure buffer depth and adjust playback rate
            let newRate = 1.0;
            let bufferSpanMs = 0;

            // Late-join catchup (screencast-friendly): compare the rendered frame's
            // offset against the newest arrived frame. Buffer-span alone doesn't
            // catch this — on sparse heartbeat streams (1 fps static screen) the
            // buffer never accumulates even when we're 2s behind live because
            // frames arrive and get consumed at matched cadence. The gap between
            // rendered and arrived is the real signal.
            const liveGapMs = this.lastArrivedOffsetMs - this.lastRenderedOffsetMs;
            if (liveGapMs > LATE_JOIN_GAP_MS
                && this.pendingFrames.length > 0
                && (now - this.lastSeekTime) > this.seekCooldownMs) {
                const latestTimestamp = this.pendingFrames.peekBack()!.timestamp;
                this.playbackStartTime = now;
                this.firstFrameTimestamp = latestTimestamp;
                this.playbackRate = 1.0;
                this.lastSeekTime = now;
                warnLog?.log(
                    `Late-join catchup: jumped to live edge, ` +
                    `lastArrivedMs=${this.lastArrivedOffsetMs.toFixed(0)}, ` +
                    `lastRenderedMs=${this.lastRenderedOffsetMs.toFixed(0)}, ` +
                    `gapMs=${liveGapMs.toFixed(0)}`);
            }

            if (this.pendingFrames.length >= 2) {
                bufferSpanMs = (this.pendingFrames.peekBack()!.timestamp
                    - this.pendingFrames.peekFront()!.timestamp) / 1000;

                if (bufferSpanMs > this.seekThresholdMs
                    && (now - this.lastSeekTime) > this.seekCooldownMs) {
                    // Hard seek fallback: if >5s behind and cooldown elapsed, jump forward
                    const latestTimestamp = this.pendingFrames.peekBack()!.timestamp;
                    this.playbackStartTime = now;
                    this.firstFrameTimestamp = latestTimestamp;
                    this.playbackRate = 1.0;
                    this.lastSeekTime = now;
                    warnLog?.log(
                        `Wall-clock hard seek: bufferSpan=${bufferSpanMs.toFixed(0)}ms, ` +
                        `pending=${this.pendingFrames.length}`);
                } else if (bufferSpanMs >= CATCHUP_AGGRESSIVE_MS) {
                    // Graduated recovery: aggressive catch-up at 1.15x
                    newRate = 1.15;
                } else if (bufferSpanMs >= CATCHUP_GENTLE_MS) {
                    // Graduated recovery: gentle catch-up at 1.05x
                    newRate = 1.05;
                }
            }

            // Rebase timing anchor when rate changes to avoid sudden jump
            if (Math.abs(newRate - this.playbackRate) > 0.005) {
                this.firstFrameTimestamp += (now - this.playbackStartTime) * 1000 * this.playbackRate;
                this.playbackStartTime = now;
                this.playbackRate = newRate;
            }

            const elapsedUs = (now - this.playbackStartTime) * 1000 * this.playbackRate;
            targetTimestamp = this.firstFrameTimestamp + elapsedUs;

            if (now - this.lastSyncLogTime > 2000) {
                this.lastSyncLogTime = now;
                debugLog?.log(
                    `wallClock: authorId=${this.authorId}, rate=${this.playbackRate.toFixed(3)}, ` +
                    `pending=${this.pendingFrames.length}, bufferSpanMs=${bufferSpanMs.toFixed(0)}`);
            }
        }

        if (this.renderFrameCount % 60 === 0) {
            debugLog?.log(
                `onRenderFrame #${this.renderFrameCount}: lastRenderedOffsetMs=${this.lastRenderedOffsetMs.toFixed(0)}, ` +
                `pendingFrames=${this.pendingFrames.length}`);
        }

        // Apply jitter buffer: subtract buffer from target so fewer frames are eligible
        // for presentation, effectively delaying rendering to absorb network jitter
        const jitterBufferUs = this.jitterBufferMs * 1000;
        const adjustedTargetTimestamp = targetTimestamp - jitterBufferUs;

        // Find the latest frame due for presentation; drop earlier ones
        let frameToRender: PendingFrame | null = null;
        while (this.pendingFrames.length > 0 && this.pendingFrames.peekFront()!.timestamp <= adjustedTargetTimestamp) {
            if (frameToRender) {
                frameToRender.close();
                this.bufferSize--;
            }
            frameToRender = this.pendingFrames.shift()!;
        }

        if (frameToRender) {
            this.bufferSize--;
            this.lastRenderedOffsetMs = frameToRender.timestamp / 1000;
            this.renderBackend.drawFrame(frameToRender);
            if (this.checkOutputVerification('render-frame')) {
                frameToRender.close();
                return;
            }
            frameToRender.close();
            this.consecutiveEmptyRenders = 0;
        } else if (this.pendingFrames.length > 0) {
            this.consecutiveEmptyRenders++;
            if (this.consecutiveEmptyRenders >= 60) {
                warnLog?.log(`Render stuck for ${this.consecutiveEmptyRenders} frames, resetting timing anchor`);
                // Anchor to actual buffer content — clock-based liveOffsetMs may be wrong
                // (e.g., after sender reconnection where startedAtMs and frame offsets diverge)
                this.playbackStartTime = performance.now();
                this.firstFrameTimestamp = this.pendingFrames.peekFront()!.timestamp;
                this.playbackRate = 1.0;
                this.consecutiveEmptyRenders = 0;
            }
        } else {
            this.consecutiveEmptyRenders = 0;
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
        const isBufferLow = this.bufferSize < 3;
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

    private checkOutputVerification(reason: string): boolean {
        if (this.outputVerified || !this.isPlaying)
            return false;

        // Reference dims = the LATEST keyframe the sender declared
        // (VideoFrameDto.Width / Height). Stream-metadata dims
        // (expectedDisplayWidth / Height) are a snapshot from stream
        // creation and CANNOT be the reference: resolution adapts
        // mid-stream (orientation, simulcast layer switch, screencast
        // resize, quality preset bump). See
        // feedback_video_dim_verification_per_frame.md.
        const refW = this.lastKeyframeWidth;
        const refH = this.lastKeyframeHeight;
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
                `OUTPUT_VERIFICATION_FAILED: decoded ${output.width}x${output.height} ` +
                `does not match latest keyframe ${refW}x${refH} ` +
                `(${reason}); codec=${this.codecCategory || 'unknown'}`);
        }

        if (this.shouldRequestCodecExclusion() && !this.codecExclusionRequested) {
            this.codecExclusionRequested = true;
            this.stopOutputVerificationMonitor();
            warnLog?.log(`OUTPUT_VERIFICATION_FAILED: requesting codec exclusion for ${this.codecCategory}`);
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
        debugLog?.log(`OUTPUT_VERIFIED: ${width}x${height} (${reason})`);
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

        // After tab restore: skip stale encoded frames arriving from the RPC stream
        if (this.skipFramesBelowOffsetMs > 0 && timestampMs < this.skipFramesBelowOffsetMs) {
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
                // After tab restore: skip keyframes that are too old
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
                this.pipelineLatencyMs = 0; // stale value causes render stall after reconfigure

                // Flush old pending frames — they're from the old decoder at stale offsets.
                // Keeping them creates a multi-second render stall (offset gap).
                while (!this.pendingFrames.isEmpty()) {
                    try { this.pendingFrames.shift()!.close(); } catch { /* already closed */ }
                }
                this.bufferSize = 0;
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
        const dataBuffer = ownedArrayBuffer(frameData);
        let descBuffer: ArrayBuffer | undefined;
        if (description && description.length > 0) {
            descBuffer = ownedArrayBuffer(description);
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
            );
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

    // Sends a render-hint-only ReportVideoLatency if the canvas-derived quality
    // level has changed since the last send. Idempotent across repeat fires from
    // the ResizeObserver. Returns the level that was sent (or undefined if
    // suppressed because nothing changed).
    private maybeSendRenderHint(): number | null | undefined {
        const level = this.computeRenderQualityLevel();
        if (level === this.lastSentRenderQuality) return undefined;
        this.lastSentRenderQuality = level;
        if (level === null) return level; // canvas not laid out yet — wait
        debugLog?.log(`RenderQuality hint: level=${level} (canvas=${this.canvas.clientWidth}x${this.canvas.clientHeight})`);
        // Hint-only mode: StreamOffsetMs=-1 tells the server to apply just the
        // render hint + visibility flag without recording a latency sample
        // (we haven't rendered a frame yet, no offset to report).
        streamingApi.streamServer.ReportVideoLatency(this.streamId, {
            StreamOffsetMs: -1,
            RenderQuality: level,
            IsVisible: typeof document !== 'undefined' && document.visibilityState === 'visible',
        }).then(r => { this.lastForwarded = r; })
            .catch((e: unknown) => warnLog?.log('Render-hint ReportVideoLatency error:', e));
        return level;
    }

    private requestKeyFrame(): void {
        const now = performance.now();
        if (now - this.lastKeyFrameRequestTime < this.keyFrameRequestCooldownMs)
            return;
        this.lastKeyFrameRequestTime = now;

        warnLog?.log(`PLI: requesting keyframe for stream ${this.streamId}`);
        streamingApi.streamServer.RequestKeyFrame(this.streamId)
            .catch((e: unknown) => warnLog?.log('RequestKeyFrame error:', e));
    }

    private onVisibilityRestored(): void {
        if (!this.decoderWorker) return;
        void this.restartAfterVisibilityChange();
    }

    private async restartAfterVisibilityChange(): Promise<void> {
        if (!this.decoderWorker) return;

        this.skippedBacklogFrames = 0;
        const pendingCount = this.pendingFrames.length;

        // Close pending decoded frames so the gated wait doesn't render stale
        // content while the next keyframe is in flight.
        while (!this.pendingFrames.isEmpty()) {
            try { this.pendingFrames.shift()!.close(); } catch { /* already closed */ }
        }
        this.bufferSize = 0;

        // Server-only skip architecture: keep the existing pull running, do
        // NOT call startPull (which forces a forward jump on the server and
        // destroys frames between currentOffset and now). Just gate deltas
        // at the worker decoder until the PLI keyframe arrives in-band.
        try { await this.decoderWorker.flagWaitingForKeyframe(); }
        catch { /* ignore */ }
        this.waitingForKeyframe = true;
        this.requestKeyFrame();

        // Reset timing anchor so playback re-syncs on next rendered frame
        this.playbackStartTime = 0;
        this.playbackRate = 1.0;
        this.lastSeekTime = 0;
        this.rebufferDelayMs = 300;
        this.lastRenderedOffsetMs = 0;
        this.lastArrivedOffsetMs = 0;

        // Chrome may have throttled the decoder while hidden — give it a
        // warmup window before SLOW_DECODE thresholds re-arm.
        this.codecSlowTickCount = 0;
        this.decoderWarmupUntilMs = performance.now() + SLOW_DECODE_WARMUP_MS;

        this.lastDiagDecodedFrames = 0;
        this.lastDiagReceivedFrames = 0;
        this.receivedFrameCount = 0;
        this.receivedBytes = 0;
        this.firstFrameReceivedTime = 0;

        this.pipelineLatencyMs = 0;
        warnLog?.log(
            `Tab restored: flushed ${pendingCount} pending frames, gating deltas until next keyframe`);
    }

    /** Called by Blazor */
    public async startPull(streamId: string, skipToMs: number): Promise<void> {
        if (!this.isPlaying) {
            warnLog?.log('startPull called but player not started');
            return;
        }

        warnLog?.log(
            `startPull DIAG: streamId=${streamId}, skipToMs=${skipToMs.toFixed(0)}, ` +
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
            warnLog?.log(`startPull DIAG: entering delegatePullToWorker (first off-thread setup)`);
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
                `startPull DIAG: SUSPICIOUS — main-thread pull starting while ` +
                `offThreadPullActive=true and renderBackend=${this.renderBackend.kind}. ` +
                `Decoded frames may not reach the visible <video>.`);
        }

        // Cancel any existing pull
        this.pullAbortController?.abort();
        const abortController = new AbortController();
        this.pullAbortController = abortController;

        const skipToTicks = secondsToMoment(skipToMs / 1000);

        warnLog?.log(`startPull [RPC]: stream=${streamId}, skipTo=${skipToMs}ms, skipToTicks=${skipToTicks}, retryCount=${this.pullRetryCount}`);

        try {
            warnLog?.log(`startPull [RPC]: calling GetVideo(${streamId}, ${skipToTicks})`);
            const stream = await streamingApi.streamServer.GetVideo(streamId, skipToTicks);
            warnLog?.log(`startPull [RPC]: GetStream returned, starting iteration`);
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
                    warnLog?.log(
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
            warnLog?.log(`startPull [RPC] ERROR: ${message}`, stack);
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
                    `FRAME_RECV: #${this.receivedFrameCount} offsetMs=${offsetMs.toFixed(0)}, ` +
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

        // Compute A/V drift. In off-thread (mstg) mode the main-thread render
        // loop never runs, so `lastRenderedOffsetMs` stays at 0 and the
        // resulting drift is meaningless (≈ −stream-elapsed). Substitute
        // `videoEl.currentTime` — the MSTG element's clock IS the rendered
        // video position. Unit: seconds → multiply by 1000 for ms.
        let avDriftMs: number | null = null;
        const audioState = AudioVideoSync.get(this.authorId);
        if (audioState) {
            const audioPlayingAtMs = AudioVideoSync.interpolatePlayingAt(audioState) * 1000;
            const targetVideoOffsetMs = (audioState.recordedAtMs - this.startedAtMs)
                + audioPlayingAtMs - this.pipelineLatencyMs;
            const renderedOffsetMs = this.renderBackend.kind === 'mstg'
                ? this.videoEl.currentTime * 1000
                : this.lastRenderedOffsetMs;
            avDriftMs = Math.round(renderedOffsetMs - targetVideoOffsetMs);
        }

        return {
            streamId: this.streamId,
            authorId: this.authorId,
            codec: this.decoderConfig?.codec ?? 'unknown',
            codecCategory: this.codecCategory,
            bitrateKbps,
            pipelineLatencyMs: Math.round(this.pipelineLatencyMs),
            jitterBufferMs: Math.round(this.jitterBufferMs),
            jitterEstimateMs: Math.round(this.jitterEstimateMs),
            smoothedRttMs: Math.round(this.smoothedRttMs),
            rttGradientMs: Math.round(this.rttGradientMs),
            playbackRate: this.playbackRate,
            bufferSize: this.pendingFrames.length,
            receivedFrameCount,
            receivedKeyframeCount,
            renderFrameCount: this.renderFrameCount,
            skipToLiveCount: this.skipToLiveCount,
            waitingForKeyframe: this.waitingForKeyframe,
            qualityReductionRequested: this.qualityReductionRequested,
            codecSlowTickCount: this.codecSlowTickCount,
            decoderStats,
            avDriftMs,
            forwarded: this.lastForwarded,
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
        warnLog?.log(
            `delegatePullToWorker DIAG: entry #${this.delegateEntryCount}, ` +
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
                warnLog?.log(
                    `delegatePullToWorker DIAG: Tier 2 main-thread MSTG constructed, ` +
                    `trackId=${generator.id}, attached via backend=${this.renderBackend.kind}`);
            } catch (e) {
                warnLog?.log('Main-thread MSTG construct failed, falling back to worker tier:', e);
                mainGenerator = null;
                mainWritable = undefined;
            }
        } else {
            warnLog?.log(`delegatePullToWorker DIAG: Tier 2 unavailable (no globalThis.MediaStreamTrackGenerator), falling back to worker tier`);
        }

        const channel = new MessageChannel();
        AudioVideoSync.subscribeWorker(this.authorId, channel.port1);
        this.offThreadSyncChannel = channel;
        const apiUrl = BrowserInit.getUrl('/rpc/ws').replace(/^http/, 'ws');
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
                warnLog?.log(`delegatePullToWorker DIAG: bgCanvas transferred to worker (first time)`);
            } catch (e) {
                warnLog?.log('transferControlToOffscreen failed for bg canvas:', e);
            }
        } else {
            warnLog?.log(
                `delegatePullToWorker DIAG: bgCanvas NOT transferred this call ` +
                `(alreadyTransferred=${this.bgOffscreenTransferred}, ` +
                `transferFn=${typeof (this.bgCanvasEl as { transferControlToOffscreen?: unknown }).transferControlToOffscreen})`);
        }
        // Snapshot ServerClock skew for the worker. Worker has no ServerClock;
        // it reconstructs server-aligned now via `Date.now() + offset`. Drift
        // over a typical call is < 50 ms, well within the ±100 ms drift-clamp
        // applied inside MstgSelector.tick(). One snapshot is enough.
        const serverClockOffsetMs = ServerClock.now() - Date.now();
        try {
            await this.decoderWorker.startPullInWorker(
                streamId, skipToMs, apiUrl,
                this.startedAtMs, serverClockOffsetMs, this.jitterBufferMs,
                channel.port2,
                mainWritable,
                bgOffscreen);
            this.offThreadPullActive = true;
            debugLog?.log(`Off-thread pull started for ${streamId}, skipTo=${skipToMs}ms (tier ${mainWritable ? 2 : 1}), serverClockOffsetMs=${serverClockOffsetMs.toFixed(0)}`);
            return true;
        } catch (e) {
            warnLog?.log('startPullInWorker rejected:', e);
            if (mainGenerator) {
                try { mainGenerator.stop(); } catch { /* ignore */ }
            }
            AudioVideoSync.unsubscribeWorker(this.authorId, channel.port1);
            try { channel.port1.close(); } catch { /* ignore */ }
            this.offThreadSyncChannel = null;
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
        const isVisible = !document.hidden;
        const renderLevel = this.computeRenderQualityLevel();
        try {
            streamingApi.streamServer.ReportVideoLatency(this.streamId, {
                StreamOffsetMs: streamOffsetMs,
                MedianDecodeTimeMs: report.medianDecodeTimeMs,
                BufferDepth: report.bufferDepth,
                BufferSpanMs: report.bufferSpanMs,
                RenderQuality: renderLevel,
                IsVisible: isVisible,
            }).then(r => { this.lastForwarded = r; })
                .catch((e: unknown) => warnLog?.log('ReportVideoLatency failed:', e));
        } catch (e) {
            warnLog?.log('ReportVideoLatency failed:', e);
        }
    }

    public async stop(): Promise<void> {
        if (!this.isPlaying) return;

        warnLog?.log(`VideoPlayer stop() called for stream ${this.streamId}, rendered=${this.renderFrameCount} frames, received=${this.receivedFrameCount}`);

        // Unregister from global diagnostics registry
        activePlayers.delete(this.streamId);
        warnLog?.log(`VideoPlayer registry: removed ${this.streamId}, active=${activePlayers.size}`);

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
        this.pipelineLatencyMs = 0;
        this.skipFramesBelowOffsetMs = 0;
        this.skippedBacklogFrames = 0;
        this.lastDiagDecodedFrames = 0;
        this.lastDiagReceivedFrames = 0;
        this.consecutiveEmptyRenders = 0;
        this.playbackRate = 1.0;
        this.lastSeekTime = 0;
        this.pullRetryCount = 0;
        this.lastLatencyReportTime = 0;
        this.lastForwarded = null;

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
        if (this.offThreadSyncChannel) {
            AudioVideoSync.unsubscribeWorker(this.authorId, this.offThreadSyncChannel.port1);
            try { this.offThreadSyncChannel.port1.close(); } catch { /* ignore */ }
            this.offThreadSyncChannel = null;
        }
        if (this.connectivityHandlerOnline) {
            ConnectivityUI.isOnlineChanged.remove(this.connectivityHandlerOnline);
            this.connectivityHandlerOnline = null;
        }
        if (this.connectivityHandlerConnected) {
            ConnectivityUI.isConnectedChanged.remove(this.connectivityHandlerConnected);
            this.connectivityHandlerConnected = null;
        }

        // Close all pending frames
        while (!this.pendingFrames.isEmpty()) {
            try {
                this.pendingFrames.shift()!.close();
            } catch {
                // Ignore
            }
        }
        this.bufferSize = 0;

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
            warnLog?.log(`reportLatencyTick: skip — lastRendered=${this.lastRenderedOffsetMs.toFixed(0)}`);
            return;
        }
        // streamOffsetMs is what we send to the server for its latency computation
        // (ServerClock.Now - (StartedAt + streamOffsetMs) = network+relay transit).
        // Use the newest arrived offset, NOT the rendered one — the render lags by
        // pipelineLatencyMs (jitter buffer) which is our local choice, not congestion.
        // Conflating them trips the server's "baseline + 200ms + 30%" step-down on a
        // healthy link once the buffer stabilizes.
        const streamOffsetMs = Math.max(this.lastArrivedOffsetMs, this.lastRenderedOffsetMs);

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
        warnLog?.log(
            `LATENCY: authorId=${this.authorId}, streamId=${this.streamId}, ` +
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
                this.pipelineLatencyMs -= reductionMs;
                warnLog?.log(
                    `CATCHUP: frameAge ${frameAgeMs.toFixed(0)}ms, reducing pipelineLatencyMs by ${reductionMs.toFixed(1)}ms to ${this.pipelineLatencyMs.toFixed(0)}ms`);
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
                    `GRADUATED_RECOVERY: frameAge ${frameAgeMs.toFixed(0)}ms > ${DROP_TO_KEYFRAME_MS}ms, dropping ${dropCount} oldest frames`);
                for (let i = 0; i < dropCount; i++) {
                    this.pendingFrames.shift()!.close();
                }
                this.bufferSize = this.pendingFrames.length;
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
                `SKIP_TO_LIVE: latency ${latencyMs.toFixed(0)}ms > ${VIDEO.skipToLiveThresholdMs}ms, gating until next keyframe (count=${this.skipToLiveCount})`);

            streamingApi.streamServer.ReportVideoLatency(this.streamId, {
                StreamOffsetMs: streamOffsetMs,
                RenderQuality: this.computeRenderQualityLevel(),
                IsVisible: document.visibilityState === 'visible',
            }).then(r => { this.lastForwarded = r; })
                .catch(() => { /* best-effort */ });

            while (!this.pendingFrames.isEmpty())
                this.pendingFrames.shift()!.close();
            this.bufferSize = 0;
            this.pipelineLatencyMs = 0;
            this.playbackStartTime = 0;
            this.lastRenderedOffsetMs = 0;
            this.lastArrivedOffsetMs = 0;
            this.lastSkipToLiveTime = performance.now();
            // Stale arrival times from before the gate are meaningless
            this.jitterEstimateMs = 0;
            this.jitterBufferMs = this.minJitterBufferMs;
            this.lastFrameArrivalTime = 0;
            this.lastFrameArrivalInterval = 0;

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

                warnLog?.log(
                    `VIDEO_DECODE: codec=${this.decoderConfig?.codec ?? 'unknown'} ` +
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
                            `SLOW_DECODE: ${inWarmup ? 'warmup' : 'hidden tab'} — ` +
                            `resetting tick count (was ${this.codecSlowTickCount})`);
                        this.codecSlowTickCount = 0;
                    }
                }
                if (isBadTick) {
                    this.codecSlowTickCount++;
                    if (!this.qualityReductionRequested && this.codecSlowTickCount >= QUALITY_REDUCTION_TICK_COUNT) {
                        // Phase 1: request quality reduction from the sender
                        warnLog?.log(
                            `SLOW_DECODE: ${this.codecSlowTickCount} consecutive bad ticks for ${this.codecCategory}, ` +
                            `requesting quality reduction (medianDecode=${ds.medianDecodeTime.toFixed(1)}ms, ` +
                            `queueDepth=${ds.decodeQueueSize})`);
                        this.qualityReductionRequested = true;
                        this.codecSlowTickCount = 0; // reset, give reduced quality time to take effect
                        void this.blazorRef.invokeMethodAsync('OnRequestQualityReduction', this.codecCategory);
                    } else if (this.qualityReductionRequested && this.codecSlowTickCount >= CODEC_EXCLUSION_TICK_COUNT
                        && this.codecCategory !== 'h264' && this.codecCategory !== 'unknown') {
                        // Phase 2: quality reduction didn't help — exclude codec entirely
                        warnLog?.log(
                            `SLOW_DECODE: codec ${this.codecCategory} too slow even after quality reduction ` +
                            `(${this.codecSlowTickCount} more bad ticks), requesting codec exclusion`);
                        void this.blazorRef.invokeMethodAsync('OnRequestCodecExclusion', this.codecCategory);
                        void this.reportEnded('Codec excluded after sustained slow decode');
                        return;
                    }
                } else {
                    if (this.codecSlowTickCount > 0) {
                        debugLog?.log(`SLOW_DECODE: reset — good tick after ${this.codecSlowTickCount} bad ticks`);
                    }
                    this.codecSlowTickCount = 0;
                    this.qualityReductionRequested = false;
                }

                // A/V sync diagnostics
                const audioState = AudioVideoSync.get(this.authorId);
                if (audioState) {
                    const audioPlayingAtMs = AudioVideoSync.interpolatePlayingAt(audioState) * 1000;
                    const targetVideoOffsetMs = (audioState.recordedAtMs - this.startedAtMs)
                        + audioPlayingAtMs - this.pipelineLatencyMs;
                    const avDriftMs = this.lastRenderedOffsetMs - targetVideoOffsetMs;
                    warnLog?.log(
                        `AV_SYNC: drift=${avDriftMs.toFixed(0)}ms ` +
                        `(videoOffset=${this.lastRenderedOffsetMs.toFixed(0)}ms, ` +
                        `targetOffset=${targetVideoOffsetMs.toFixed(0)}ms, ` +
                        `audioPlayingAt=${audioPlayingAtMs.toFixed(0)}ms, ` +
                        `audioState=${audioState.playbackState})`);
                } else {
                    warnLog?.log(`AV_SYNC: no audio state for authorId=${this.authorId}`);
                }

                // Send enriched latency report with diagnostics + RTT measurement
                const sendTime = performance.now();
                streamingApi.streamServer.ReportVideoLatency(this.streamId, {
                    StreamOffsetMs: streamOffsetMs,
                    MedianDecodeTimeMs: ds.pureMedianDecodeTime >= 0 ? ds.pureMedianDecodeTime : ds.medianDecodeTime,
                    BufferDepth: this.pendingFrames.length,
                    BufferSpanMs: currentBufferSpanMs,
                    RenderQuality: this.computeRenderQualityLevel(),
                    IsVisible: document.visibilityState === 'visible',
                }).then(r => {
                    this.lastForwarded = r;
                    this.updateRttEstimate(performance.now() - sendTime);
                }).catch((e: unknown) => {
                    warnLog?.log('ReportVideoLatency invoke error:', e);
                });
            });
        } else {
            // No decoder worker — send basic report without diagnostics + RTT measurement
            const sendTime = performance.now();
            streamingApi.streamServer.ReportVideoLatency(this.streamId, {
                StreamOffsetMs: streamOffsetMs,
                RenderQuality: this.computeRenderQualityLevel(),
                IsVisible: document.visibilityState === 'visible',
            }).then(r => {
                this.lastForwarded = r;
                this.updateRttEstimate(performance.now() - sendTime);
            }).catch((e: unknown) => {
                warnLog?.log('ReportVideoLatency invoke error:', e);
            });
        }
    }

    // Maps this player's current render size to a VideoQualityLevel hint for the
    // server's simulcast fan-out. Uses canvas.clientWidth (actual layout pixels)
    // rather than canvas.width (decoder output resolution). Server maps Low→spatial
    // layer 0, Medium→1, High/Full/Ultra→2. Returns null when the canvas has no
    // layout yet (detached or hidden) so the server applies no render cap.
    private computeRenderQualityLevel(): number | null {
        return renderQualityLevelForWidth(this.canvas.clientWidth);
    }

    private updateRttEstimate(rttMs: number): void {
        this.previousRttMs = this.smoothedRttMs;
        this.smoothedRttMs = this.smoothedRttMs === 0 ? rttMs : 0.8 * this.smoothedRttMs + 0.2 * rttMs;
        this.rttGradientMs = this.smoothedRttMs - this.previousRttMs;

        // Proactive congestion detection: RTT increasing rapidly
        if (this.rttGradientMs > 50 && this.smoothedRttMs > 100) {
            warnLog?.log(
                `RTT_GRADIENT: rtt=${this.smoothedRttMs.toFixed(0)}ms, gradient=${this.rttGradientMs.toFixed(0)}ms — congestion detected`);
            // Proactively request quality reduction before latency threshold is hit
            if (!this.qualityReductionRequested && this.codecCategory) {
                void this.blazorRef.invokeMethodAsync('OnRequestQualityReduction', this.codecCategory);
                this.qualityReductionRequested = true;
            }
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

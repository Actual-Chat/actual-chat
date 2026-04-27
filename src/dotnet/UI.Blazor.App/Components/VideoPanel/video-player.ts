import { getLogs } from 'logging';
import { initVideoRpc } from '../../Services/Video/streaming-rpc-client';
import { Api, streamingApi, type VideoFrameDto } from 'api';
import { ServerClock } from 'server-clock';
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import { AudioVideoSync } from 'audio-video-sync';
import { DocumentEvents } from 'event-handling';
import { Versioning } from 'versioning';
import { type Subscription } from 'rxjs';
import { renderQualityLevelForWidth } from './render-quality';
import type { DecoderWorker } from '../../Services/Video/workers/decoder-worker-contract';
import type { DecoderConfig, DecoderStats } from '../../Services/Video/webcodecs-decoder';
import {
    createInputChannel,
    type RawChunkMessage,
    type StreamEndpoints,
    supportsTransferableStreams,
} from '../../Services/Video/workers/stream-channel';

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
}

const { debugLog, warnLog, errorLog } = getLogs('VideoPlayer');

// Skip-to-live: client detects high latency and re-requests stream from next keyframe
const SKIP_TO_LIVE_THRESHOLD_MS = 3000; // Matches Constants.Video.SkipToLiveThresholdMs

// Graduated recovery thresholds — escalating response to growing latency
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
const SLOW_DECODE_TIME_THRESHOLD_MS = 100; // 3x the 33ms/frame budget at 30fps
const SLOW_DECODE_QUEUE_THRESHOLD = 10;    // Normal is 0-1; 10+ means decoder is ~300ms behind
const QUALITY_REDUCTION_TICK_COUNT = 5;    // ~10s of sustained bad performance → request quality reduction
const CODEC_EXCLUSION_TICK_COUNT = 30;     // ~60s after quality reduction still bad → exclude codec
// SLOW_DECODE warmup: skip the first window of samples after decoder init / codec
// switch / tab-restore. Cold-start times for codec init + first keyframe routinely
// exceed the per-frame budget (200–600 ms) before steady-state hits the sub-ms median;
// counting those samples against codec health triggers spurious exclusions.
const SLOW_DECODE_WARMUP_MS = 5000;
const LATENCY_REPORT_INTERVAL_MS = 2000; // Matches Constants.Video.LatencyReportInterval

interface PendingFrame {
    drawable: VideoFrame | ImageBitmap;
    timestamp: number;
    displayWidth: number;
    displayHeight: number;
    close(): void;
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
    private canvasCtx: CanvasRenderingContext2D | null = null;
    // Decoder worker (off-main-thread decoding)
    private decoderWorkerInstance: Worker | null = null;
    private decoderWorker: (DecoderWorker & Disposable) | null = null;
    private decoderConfig: DecoderConfig | null = null;
    private pendingFrames: PendingFrame[] = [];
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

    // Audio sync
    private startedAtMs: number;

    // Stream mode state
    private readonly useStreams: boolean;
    private chunkInputChannel: StreamEndpoints<RawChunkMessage> | null = null;

    /** Creates a new VideoPlayer instance for Blazor interop */
    static create(
        canvas: HTMLCanvasElement,
        blazorRef: DotNet.DotNetObject,
        streamId: string,
        authorId: string,
        codec: string,
        width: number,
        height: number,
        codecSettings: string,
        startedAtMs: number
    ): VideoPlayer {
        return new VideoPlayer(blazorRef, streamId, authorId, codec, width, height, codecSettings, canvas, startedAtMs);
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
        startedAtMs: number
    ) {
        this.blazorRef = blazorRef;
        this.streamId = streamId;
        this.authorId = authorId;
        this.startedAtMs = startedAtMs;
        this.canvas = canvas;
        this.canvasCtx = canvas.getContext('2d');
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

        // Initialize decoder worker
        void this.initDecoderWorker(codec, width, height, codecSettings);
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
            const candidates = this.getCodecCandidates(codec, description);
            debugLog?.log(`Codec candidates: [${candidates.join(', ')}]`);

            // Try each candidate with hardware preference, then software fallback
            let codecString: string | null = null;
            let bestAcceleration: HardwareAcceleration = 'prefer-hardware';
            for (const candidate of candidates) {
                for (const accel of ['prefer-hardware', 'no-preference'] as const) {
                    try {
                        const { supported } = await VideoDecoder.isConfigSupported({
                            codec: candidate,
                            hardwareAcceleration: accel,
                        });
                        if (supported) {
                            codecString = candidate;
                            bestAcceleration = accel;
                            break;
                        }
                    } catch { /* continue to next */ }
                }
                if (codecString) break;
            }
            if (!codecString) {
                warnLog?.log(`No supported codec found among candidates: [${candidates.join(', ')}]`);
                this.isPlaying = false;
                void this.reportEnded(`Codec not supported`);
                return;
            }
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
            };

            // Create decoder worker
            const decoderWorkerPath = Versioning.mapPath('/dist/videoDecoderWorker.js');
            this.decoderWorkerInstance = new Worker(decoderWorkerPath, { type: 'module' });
            this.decoderWorkerInstance.onerror = (e) => errorLog?.log('Decoder worker error:', e);

            // Create RPC proxy (used for control messages in both modes + data path in fallback)
            this.decoderWorker = rpcClientServer<DecoderWorker>(
                'VideoPlayer.decoder',
                this.decoderWorkerInstance,
                { onDecodedFrame: (frame: VideoFrame) => { this.onFrameDecoded(frame); return Promise.resolve(); } }
            );

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

    /**
     * Build an ordered list of codec string candidates for decoder configuration.
     * For HEVC: tries description-derived (ground truth), then stream metadata, then hardcoded fallback.
     * For other codecs: returns a single candidate from the legacy mapping.
     */
    private getCodecCandidates(codec: string, description?: ArrayBuffer): string[] {
        if (description && description.byteLength >= 5 && VideoPlayer.isHvccDescription(description)) {
            const bytes = new Uint8Array(description);
            const generalProfileIdc = bytes[1] & 0x1F;
            const tier = (bytes[1] >> 5) & 0x01;
            const tierStr = tier ? 'H' : 'L';
            const generalLevelIdc = bytes[12]; // actual level from encoder

            const candidates: string[] = [];
            const seen = new Set<string>();
            const addCandidate = (c: string) => {
                if (!seen.has(c)) {
                    seen.add(c);
                    candidates.push(c);
                }
            };

            // 1. Derived from HVCC binary description (ground truth from encoder) — both hev1 and hvc1 prefixes
            addCandidate(`hev1.${generalProfileIdc}.6.${tierStr}${generalLevelIdc}.B0`);
            addCandidate(`hvc1.${generalProfileIdc}.6.${tierStr}${generalLevelIdc}.B0`);

            // 2. Stream metadata codec string (sender's declared codec, if it's a full HEVC string)
            const lc = codec.toLowerCase();
            if (lc.startsWith('hev1.') || lc.startsWith('hvc1.')) {
                addCandidate(codec);
            }

            // 3. Hardcoded Level 4.0 fallback (safe default for buggy HW encoders
            //    that write incorrect levels causing Chrome SW decode fallback) — both prefixes
            addCandidate(`hev1.${generalProfileIdc}.6.${tierStr}120.B0`);
            addCandidate(`hvc1.${generalProfileIdc}.6.${tierStr}120.B0`);

            const declaredLower = codec.toLowerCase();
            if (!declaredLower.startsWith('hev1') && !declaredLower.startsWith('hvc1')
                && declaredLower !== 'hevc' && declaredLower !== 'h265') {
                warnLog?.log(`Codec mismatch: declared=${codec} but description is HVCC`);
            }

            return candidates;
        }

        // Non-HEVC codecs: single candidate from legacy mapping
        return [this.mapCodecToWebCodecs(codec, description)];
    }

    private mapCodecToWebCodecs(codec: string, description?: ArrayBuffer): string {
        // Derive H.264 codec string from avcC description bytes
        if (description && description.byteLength >= 5 && VideoPlayer.isAvcCDescription(description)) {
            const bytes = new Uint8Array(description);
            const profileIndication = bytes[1];
            const profileCompatibility = bytes[2];
            const levelIndication = bytes[3];
            const codecString = `avc1.${profileIndication.toString(16).padStart(2, '0')}${profileCompatibility.toString(16).padStart(2, '0')}${levelIndication.toString(16).padStart(2, '0')}`;
            const declaredLower = codec.toLowerCase();
            if (declaredLower !== 'h264' && declaredLower !== 'avc1' && !declaredLower.startsWith('avc1.')) {
                warnLog?.log(`Codec mismatch: declared=${codec} but description is avcC, overriding to ${codecString}`);
            }
            return codecString;
        }

        // If we have an avcC description and declared H.264, extract the actual profile
        if (description && description.byteLength >= 4 && (codec.toLowerCase() === 'h264' || codec.toLowerCase() === 'avc1')) {
            const bytes = new Uint8Array(description);
            const profileIndication = bytes[1];
            const profileCompatibility = bytes[2];
            const levelIndication = bytes[3];
            const codecString = `avc1.${profileIndication.toString(16).padStart(2, '0')}${profileCompatibility.toString(16).padStart(2, '0')}${levelIndication.toString(16).padStart(2, '0')}`;
            debugLog?.log(`Built codec string from avcC: ${codecString}`);
            return codecString;
        }

        // Map common codec names to WebCodecs codec strings
        const codecMap: Record<string, string> = {
            'h264': 'avc1.640028',
            'avc1': 'avc1.640028',
            'h265': 'hvc1.1.6.L93.90',
            'hevc': 'hvc1.1.6.L93.90',
            'vp8': 'vp8',
            'vp9': 'vp09.00.31.08',
            'av1': 'av01.0.01M.08',
        };

        const lowerCodec = codec.toLowerCase();
        if (codecMap[lowerCodec]) {
            return codecMap[lowerCodec];
        }
        if (codec.includes('.')) {
            return codec;
        }
        return 'avc1.640028';
    }

    /**
     * Detect HEVC HVCC (HEVCDecoderConfigurationRecord) format.
     * HVCC structure (ISO 14496-15):
     *   byte[0]  = configurationVersion (must be 1)
     *   byte[1]  = general_profile_space(2) | general_tier_flag(1) | general_profile_idc(5)
     *   byte[2..5]  = general_profile_compatibility_flags (4 bytes)
     *   byte[6..11] = general_constraint_indicator_flags (6 bytes)
     *   byte[12] = general_level_idc
     *   ...minimum 23 bytes total before nalu arrays
     */

    private static getCodecCategory(codecString: string): string {
        const lc = codecString.toLowerCase();
        if (lc.startsWith('hev1') || lc.startsWith('hvc1')) return 'hevc';
        if (lc.startsWith('av01')) return 'av1';
        if (lc.startsWith('vp09') || lc.startsWith('vp9')) return 'vp9';
        if (lc.startsWith('avc1') || lc.startsWith('h264')) return 'h264';
        return 'unknown';
    }

    private static isHvccDescription(description: ArrayBuffer): boolean {
        // HVCC minimum size is 23 bytes (header before nalu arrays)
        if (description.byteLength < 23) return false;
        const bytes = new Uint8Array(description);
        // configurationVersion must be 1
        if (bytes[0] !== 0x01) return false;
        // general_profile_idc: valid HEVC profiles are 1 (Main), 2 (Main10), 3 (MainStillPicture), 4 (Range Extensions), 5 (High Throughput)
        const generalProfileIdc = bytes[1] & 0x1F;
        if (generalProfileIdc === 0 || generalProfileIdc > 11) return false;
        // Discriminator vs avcC: in avcC byte[5] = (0xE0 | numSPS) where top 3 bits are always 1
        // In HVCC byte[5] is part of general_profile_compatibility_flags — no such constraint
        // Also in avcC byte[4] = (0xFC | lengthSizeMinusOne) where top 6 bits are always 1
        // In HVCC byte[4] is part of general_profile_compatibility_flags — no such constraint
        // Use the avcC byte[5] marker as negative discriminator:
        // If byte[4] has top 6 bits set AND byte[5] has top 3 bits set, this looks like avcC, not HVCC
        if ((bytes[4] & 0xFC) === 0xFC && (bytes[5] & 0xE0) === 0xE0) return false;
        // general_level_idc at byte[12] should be reasonable (30-186 for common HEVC levels)
        const generalLevelIdc = bytes[12];
        if (generalLevelIdc === 0) return false;
        return true;
    }

    private static isAvcCDescription(description: ArrayBuffer): boolean {
        if (description.byteLength < 7) return false;
        const bytes = new Uint8Array(description);
        // avcC configurationVersion must be 1
        if (bytes[0] !== 0x01) return false;
        // byte[1] = AVCProfileIndication — valid H.264 profiles
        const validProfiles = [66, 77, 88, 100, 110, 122, 244];
        if (!validProfiles.includes(bytes[1])) return false;
        // byte[4] = 0xFC | (lengthSizeMinusOne & 0x03) — top 6 bits must be set
        if ((bytes[4] & 0xFC) !== 0xFC) return false;
        // byte[5] = 0xE0 | (numOfSequenceParameterSets & 0x1F) — top 3 bits must be set
        if ((bytes[5] & 0xE0) !== 0xE0) return false;
        return true;
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
                ? (this.pendingFrames[this.pendingFrames.length - 1].timestamp - this.pendingFrames[0].timestamp) / 1000
                : 0;
            if (bufferSpanMs > 600) {
                const targetSpanUs = 300 * 1000; // keep ~300ms worth of frames
                const cutoffTimestamp = this.pendingFrames[this.pendingFrames.length - 1].timestamp - targetSpanUs;
                let dropCount = 0;
                while (this.pendingFrames.length > 1 && this.pendingFrames[0].timestamp < cutoffTimestamp) {
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

        if (this.isSafari) {
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
            const rawTargetVideoOffsetMs = (audioState.recordedAtMs - this.startedAtMs) + audioPlayingAtMs;
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
                const oldestBufferedMs = this.pendingFrames[0].timestamp / 1000;
                if (rawTargetVideoOffsetMs < 0 || targetVideoOffsetMs < oldestBufferedMs - 2000) {
                    targetTimestamp = this.pendingFrames[this.pendingFrames.length - 1].timestamp;
                }
            }

            this.playbackStartTime = now;
            this.firstFrameTimestamp = targetTimestamp;

            // Safety cap: flush old frames if buffer span exceeds 2s even in audio-sync mode.
            // This prevents buffer bloat from bursty delivery causing unbounded latency growth.
            if (this.pendingFrames.length >= 2) {
                const bufferSpanMs = (this.pendingFrames[this.pendingFrames.length - 1].timestamp
                    - this.pendingFrames[0].timestamp) / 1000;
                if (bufferSpanMs > 2000) {
                    // Find the frame closest to target and drop everything before it
                    let flushIdx = 0;
                    for (let i = 0; i < this.pendingFrames.length; i++) {
                        if (this.pendingFrames[i].timestamp <= targetTimestamp) {
                            flushIdx = i;
                        } else {
                            break;
                        }
                    }
                    if (flushIdx > 0) {
                        for (let i = 0; i < flushIdx; i++) {
                            this.pendingFrames[i].close();
                            this.bufferSize--;
                        }
                        this.pendingFrames.splice(0, flushIdx);
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
                const latestTimestamp = this.pendingFrames[this.pendingFrames.length - 1].timestamp;
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
                bufferSpanMs = (this.pendingFrames[this.pendingFrames.length - 1].timestamp
                    - this.pendingFrames[0].timestamp) / 1000;

                if (bufferSpanMs > this.seekThresholdMs
                    && (now - this.lastSeekTime) > this.seekCooldownMs) {
                    // Hard seek fallback: if >5s behind and cooldown elapsed, jump forward
                    const latestTimestamp = this.pendingFrames[this.pendingFrames.length - 1].timestamp;
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
        while (this.pendingFrames.length > 0 && this.pendingFrames[0].timestamp <= adjustedTargetTimestamp) {
            if (frameToRender) {
                frameToRender.close();
                this.bufferSize--;
            }
            frameToRender = this.pendingFrames.shift()!;
        }

        if (frameToRender) {
            this.bufferSize--;
            this.lastRenderedOffsetMs = frameToRender.timestamp / 1000;
            this.drawFrame(frameToRender);
            frameToRender.close();
            this.consecutiveEmptyRenders = 0;
        } else if (this.pendingFrames.length > 0) {
            this.consecutiveEmptyRenders++;
            if (this.consecutiveEmptyRenders >= 60) {
                warnLog?.log(`Render stuck for ${this.consecutiveEmptyRenders} frames, resetting timing anchor`);
                // Anchor to actual buffer content — clock-based liveOffsetMs may be wrong
                // (e.g., after sender reconnection where startedAtMs and frame offsets diverge)
                this.playbackStartTime = performance.now();
                this.firstFrameTimestamp = this.pendingFrames[0].timestamp;
                this.playbackRate = 1.0;
                this.consecutiveEmptyRenders = 0;
            }
        } else {
            this.consecutiveEmptyRenders = 0;
        }

        this.updateBufferState();

        // Report latency from RAF — naturally pauses when tab is hidden,
        // preventing stale reports that trigger server-side skip-to-live
        if (now - this.lastLatencyReportTime >= LATENCY_REPORT_INTERVAL_MS) {
            this.lastLatencyReportTime = now;
            this.reportLatencyTick();
        }
    }

    private drawFrame(pf: PendingFrame): void {
        if (!this.canvasCtx) return;
        try {
            if (this.canvas.width !== pf.displayWidth || this.canvas.height !== pf.displayHeight) {
                this.canvas.width = pf.displayWidth;
                this.canvas.height = pf.displayHeight;
                debugLog?.log(`Canvas resized to ${pf.displayWidth}x${pf.displayHeight}`);
            }
            this.canvasCtx.drawImage(pf.drawable as CanvasImageSource, 0, 0);
        } catch (error) {
            errorLog?.log('Error rendering frame:', error);
        }
    }

    private updateBufferState(): void {
        const isBufferLow = this.bufferSize < 3;
        if (isBufferLow !== this.lastReportedBufferLow) {
            this.lastReportedBufferLow = isBufferLow;
            void this.reportPlaying(0, isBufferLow);
        }
    }

    public pushFrame(
        frameData: Uint8Array,
        timestampMs: number,
        durationMs: number,
        isKeyFrame: boolean,
        description?: Uint8Array
    ): void {
        if (!this.isPlaying || !this.decoderWorker) {
            return;
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
                    const derivedCodec = this.mapCodecToWebCodecs(
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
                this.sendToDecoderWorker(frameData, timestampMs, durationMs, isKeyFrame, description);
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
                    const derivedCodec = this.mapCodecToWebCodecs(
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
                for (const frame of this.pendingFrames) {
                    try { frame.close(); } catch { /* already closed */ }
                }
                this.pendingFrames = [];
                this.bufferSize = 0;
            }
        }

        this.sendToDecoderWorker(frameData, timestampMs, durationMs, isKeyFrame, description);
    }

    private sendToDecoderWorker(
        frameData: Uint8Array,
        timestampMs: number,
        durationMs: number,
        isKeyFrame: boolean,
        description?: Uint8Array
    ): void {
        if (!this.decoderWorker) return;

        // Copy data to transferable ArrayBuffer
        const dataBuffer = new ArrayBuffer(frameData.byteLength);
        new Uint8Array(dataBuffer).set(frameData);

        let descBuffer: ArrayBuffer | undefined;
        if (description && description.length > 0) {
            descBuffer = new ArrayBuffer(description.byteLength);
            new Uint8Array(descBuffer).set(description);
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
            });
        } else {
            // RPC fallback: send raw bytes to worker
            void this.decoderWorker.decodeRawChunk(
                timestampMs * 1000, // ms → μs
                durationMs * 1000,  // ms → μs
                isKeyFrame,
                this.sequenceNumber++,
                dataBuffer,
                descBuffer,
                rpcNoWait
            );
        }
    }


    public start(): void {
        if (this.isPlaying) return;

        this.isPlaying = true;
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
        // The ResizeObserver can fire BEFORE startPull initializes the streaming
        // RPC client (canvas layout happens during the same animation frame
        // VideoPlayer.start runs in). initVideoRpc is idempotent — calling here
        // ensures the streaming proxy is ready regardless of which entry point
        // runs first.
        initVideoRpc();
        // Hint-only mode: StreamOffsetMs=-1 tells the server to apply just the
        // render hint + visibility flag without recording a latency sample
        // (we haven't rendered a frame yet, no offset to report).
        streamingApi.streamServer.ReportVideoLatency(this.streamId, {
            StreamOffsetMs: -1,
            RenderQuality: level,
            IsVisible: typeof document !== 'undefined' && document.visibilityState === 'visible',
        }).catch((e: unknown) => warnLog?.log('Render-hint ReportVideoLatency error:', e));
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

        this.skippedBacklogFrames = 0;
        const pendingCount = this.pendingFrames.length;

        // Close all pending decoded frames to avoid burst playback
        for (const frame of this.pendingFrames) {
            try { frame.close(); } catch { /* already closed */ }
        }
        this.pendingFrames = [];
        this.bufferSize = 0;

        // Reset decoder worker to flush its internal queue
        void this.decoderWorker.resetDecoder();

        // After reset, delta frames are useless — need a fresh keyframe
        this.waitingForKeyframe = true;
        this.requestKeyFrame();

        // Reset timing anchor so playback re-syncs on next rendered frame
        this.playbackStartTime = 0;
        this.playbackRate = 1.0;
        this.lastSeekTime = 0;
        this.rebufferDelayMs = 300;
        // Reset lastRenderedOffsetMs so reportLatencyTick skips until a fresh frame renders
        // (prevents SKIP_TO_LIVE loop from using stale offset after background→foreground)
        this.lastRenderedOffsetMs = 0;
        this.lastArrivedOffsetMs = 0;

        // Reset decode performance tracking — Chrome may have throttled the decoder while hidden
        this.codecSlowTickCount = 0;
        this.decoderWarmupUntilMs = performance.now() + SLOW_DECODE_WARMUP_MS;

        // Reset diagnostic counters: the decoder reset zeroes its frame counter,
        // but lastDiag* still holds the pre-reset value. The next VIDEO_DECODE
        // diff would compute (small new value) - (large old value) = negative,
        // producing log lines like `recv=257 decoded=-2071` after tab-restore.
        this.lastDiagDecodedFrames = 0;
        this.lastDiagReceivedFrames = 0;
        this.receivedFrameCount = 0;
        this.receivedBytes = 0;
        this.firstFrameReceivedTime = 0;

        // Dispose current pull and re-request from live offset (skip-to-live)
        this.pullAbortController?.abort();
        this.pullAbortController = null;
        this.pipelineLatencyMs = 0;
        const skipToMs = Math.max(0, ServerClock.now() - this.startedAtMs);
        warnLog?.log(
            `Tab restored: flushed ${pendingCount} pending frames, decoder reset, ` +
            `re-requesting stream from offset ${skipToMs.toFixed(0)}ms`);
        void this.startPull(this.streamId, skipToMs);
    }

    /** Called by Blazor */
    public async startPull(streamId: string, skipToMs: number): Promise<void> {
        if (!this.isPlaying) {
            warnLog?.log('startPull called but player not started');
            return;
        }

        // Cancel any existing pull
        this.pullAbortController?.abort();
        const abortController = new AbortController();
        this.pullAbortController = abortController;

        initVideoRpc();
        const skipToTicks = Math.round(skipToMs * 10000); // ms → .NET TimeSpan ticks (must be integer for MessagePack int64)

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
            const offsetMs = frame.Offset / 10000;   // .NET ticks → ms
            const durationMs = frame.Duration / 10000;
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

            this.pushFrame(data, offsetMs, durationMs, isKeyFrame, description);
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

        // Compute incoming bitrate
        const elapsedSec = this.firstFrameReceivedTime > 0
            ? (performance.now() - this.firstFrameReceivedTime) / 1000
            : 0;
        const bitrateKbps = elapsedSec > 0
            ? Math.round(this.receivedBytes * 8 / elapsedSec / 1000)
            : 0;

        // Compute A/V drift
        let avDriftMs: number | null = null;
        const audioState = AudioVideoSync.get(this.authorId);
        if (audioState) {
            const audioPlayingAtMs = AudioVideoSync.interpolatePlayingAt(audioState) * 1000;
            const targetVideoOffsetMs = (audioState.recordedAtMs - this.startedAtMs)
                + audioPlayingAtMs - this.pipelineLatencyMs;
            avDriftMs = Math.round(this.lastRenderedOffsetMs - targetVideoOffsetMs);
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
            receivedFrameCount: this.receivedFrameCount,
            receivedKeyframeCount: this.receivedKeyframeCount,
            renderFrameCount: this.renderFrameCount,
            skipToLiveCount: this.skipToLiveCount,
            waitingForKeyframe: this.waitingForKeyframe,
            qualityReductionRequested: this.qualityReductionRequested,
            codecSlowTickCount: this.codecSlowTickCount,
            decoderStats,
            avDriftMs,
        };
    }

    public async stop(): Promise<void> {
        if (!this.isPlaying) return;

        warnLog?.log(`VideoPlayer stop() called for stream ${this.streamId}, rendered=${this.renderFrameCount} frames, received=${this.receivedFrameCount}`);

        // Unregister from global diagnostics registry
        activePlayers.delete(this.streamId);
        warnLog?.log(`VideoPlayer registry: removed ${this.streamId}, active=${activePlayers.size}`);

        this.isPlaying = false;
        this.stopRenderLoop();
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

        // Close all pending frames
        for (const frame of this.pendingFrames) {
            try {
                frame.close();
            } catch {
                // Ignore
            }
        }
        this.pendingFrames = [];
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
        if (frameAgeMs > DROP_TO_KEYFRAME_MS && frameAgeMs <= SKIP_TO_LIVE_THRESHOLD_MS) {
            // Phase 2: Drop oldest frames to catch up quickly.
            // PendingFrame (decoded VideoFrame/ImageBitmap) lacks isKeyFrame metadata,
            // so we can't do keyframe-aware dropping — drop the oldest half instead.
            const dropCount = Math.floor(this.pendingFrames.length / 2);
            if (dropCount > 0) {
                warnLog?.log(
                    `GRADUATED_RECOVERY: frameAge ${frameAgeMs.toFixed(0)}ms > ${DROP_TO_KEYFRAME_MS}ms, dropping ${dropCount} oldest frames`);
                for (let i = 0; i < dropCount; i++) {
                    this.pendingFrames[i].close();
                }
                this.pendingFrames.splice(0, dropCount);
                this.bufferSize = this.pendingFrames.length;
            }
        }
        // SKIP_TO_LIVE triggers on NETWORK latency (latencyMs = arrival vs sender),
        // not frameAge — on screencast, frameAge can hit 1.5s just from heartbeat
        // pacing on a perfectly healthy link, and re-requesting the stream would
        // be pointless churn. Arrival latency only grows when the stream is actually
        // stalled server-side or the network is congested.
        else if (latencyMs > SKIP_TO_LIVE_THRESHOLD_MS) {
            // Phase 3: Nuclear — re-request stream from live offset
            this.skipToLiveCount++;
            warnLog?.log(
                `SKIP_TO_LIVE: latency ${latencyMs.toFixed(0)}ms > ${SKIP_TO_LIVE_THRESHOLD_MS}ms, re-requesting stream (count=${this.skipToLiveCount})`);

            // Report the high latency to the server BEFORE resetting state,
            // so EvaluateQuality can detect that this peer is struggling and step down sender quality.
            streamingApi.streamServer.ReportVideoLatency(this.streamId, {
                StreamOffsetMs: streamOffsetMs,
                RenderQuality: this.computeRenderQualityLevel(),
                IsVisible: document.visibilityState === 'visible',
            }).catch(() => { /* best-effort */ });

            this.pullAbortController?.abort();
            this.pullAbortController = null;
            for (const pf of this.pendingFrames)
                pf.close();
            this.pendingFrames.length = 0;
            this.bufferSize = 0;
            if (this.decoderWorker) {
                void this.decoderWorker.resetDecoder();
            }
            this.waitingForKeyframe = true;
            this.requestKeyFrame();
            this.pipelineLatencyMs = 0;
            this.playbackStartTime = 0;
            // Prevent stale latency report before first new frame renders
            this.lastRenderedOffsetMs = 0;
            this.lastArrivedOffsetMs = 0;
            this.lastSkipToLiveTime = performance.now();
            // Reset jitter buffer state — stale arrival times from old stream are meaningless
            this.jitterEstimateMs = 0;
            this.jitterBufferMs = this.minJitterBufferMs;
            this.lastFrameArrivalTime = 0;
            this.lastFrameArrivalInterval = 0;
            const skipToMs = Math.max(0, ServerClock.now() - this.startedAtMs);
            void this.startPull(this.streamId, skipToMs);
            return;
        }

        // Collect decoder diagnostics and send enriched latency report
        if (this.decoderWorker) {
            void this.decoderWorker.getStats().then(ds => {
                const recvDelta = this.receivedFrameCount - this.lastDiagReceivedFrames;
                const decodedDelta = ds.decodedFrames - this.lastDiagDecodedFrames;
                this.lastDiagReceivedFrames = this.receivedFrameCount;
                this.lastDiagDecodedFrames = ds.decodedFrames;

                // Compute buffer span (time range of buffered frames)
                let currentBufferSpanMs = 0;
                if (this.pendingFrames.length >= 2) {
                    currentBufferSpanMs = (this.pendingFrames[this.pendingFrames.length - 1].timestamp
                        - this.pendingFrames[0].timestamp) / 1000;
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
                    && (ds.medianDecodeTime > SLOW_DECODE_TIME_THRESHOLD_MS
                        || ds.decodeQueueSize > SLOW_DECODE_QUEUE_THRESHOLD);
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
                            `(${this.codecSlowTickCount} more bad ticks), excluding codec`);
                        void this.blazorRef.invokeMethodAsync('OnCodecTooSlow', this.codecCategory);
                        void this.reportEnded('Codec too slow for realtime playback');
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
                }).then(() => {
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
            }).then(() => {
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

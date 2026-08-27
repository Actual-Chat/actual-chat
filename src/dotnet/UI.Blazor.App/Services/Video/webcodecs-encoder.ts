import { getLogs } from 'logging';
import Denque from 'denque';
import type { MonotonicTime } from 'clocks';
import { awaitHwReleased, getCodecCategory, getCodecForCategory } from './codec-support';

const { infoLog, warnLog, errorLog } = getLogs('VideoEncoder');

// Dedupe error logs by key within a 1s window — guards against cascades from
// the WebCodecs error callback firing faster than the dead-encoder watchdog reacts.
const ERROR_LOG_DEDUPE_WINDOW_MS = 1000;
const errorLogLastSeenMs = new Map<string, number>();
function shouldLogEncoderError(key: string): boolean {
    const now = performance.now();
    const last = errorLogLastSeenMs.get(key) ?? 0;
    if (now - last < ERROR_LOG_DEDUPE_WINDOW_MS) return false;
    errorLogLastSeenMs.set(key, now);
    return true;
}

export interface EncoderConfig {
  codec: string;
  width: number;
  height: number;
  bitrate: number;
  framerate: number;
  // GOP size in frames (primary keyframe trigger).
  keyframeInterval: number;
  // Wall-clock keyframe floor — guarantees a keyframe at least every N ms when
  // encode() is called slowly (VAD-reduced, low-framerate screencast).
  maxKeyFrameIntervalMs?: number;
  latencyMode: 'realtime' | 'quality';
  hardwareAcceleration: 'prefer-hardware' | 'prefer-software' | 'no-preference';
  // Disabled by default — HW encoders accept RGBA natively.
  preConvertYuv?: boolean;
}

export interface EncodedChunkData {
  // Set by encoder worker, e.g. 'avc1.640028', 'av01.0.08M.08'.
  codec?: string;
  chunk: EncodedVideoChunk;
  metadata: EncodedVideoChunkMetadata | undefined;
  timestamp: number;
  type: 'key' | 'delta';
  byteLength: number;
  sequenceNumber: number;
  encodeTimeMs: number;
  // Simulcast: 0=base (lowest-res); always 0 for single-encoder (P2P).
  layerId?: number;
  // Dims of the producing encoder instance (per-layer truth, not primary config).
  width: number;
  height: number;
  // Sender-side MonotonicClock snapshot at encode() time. Threaded via parallel
  // FIFO. Wire uses .timeMs (Unix ms) as offset and .epoch as discontinuity marker.
  capturedAt?: MonotonicTime;
}

export interface EncoderStats {
  encodedFrames: number;
  droppedFrames: number;
  keyFrames: number;
  totalBytes: number;
  averageEncodeTime: number;
  medianEncodeTime: number;
  // Sampled only when encode queue was empty at start; -1 if no samples.
  pureMedianEncodeTime: number;
  configuredWidth: number;
  configuredHeight: number;
  configuredBitrate: number;
  hardwareAcceleration: string;
  state: CodecState;
  reconfigureCount: number;
  // Underlying VideoEncoder close+new count (covers dim-change reconfigure + switchCodec).
  replaceCount: number;
  lastReconfigureSummary: string;
  lastReconfigureAgeMs: number;
  lastErrorName: string;
  lastErrorMessage: string;
  lastErrorAgeMs: number;
  // Pre-dedupe.
  errorCount: number;
}

export class WebCodecsEncoder {
    private encoder: VideoEncoder;
    private frameCount = 0;
    private droppedFrames = 0;
    // Coalesces the per-frame mismatch warn into one log per reconfigure-race burst.
    private inDimsMismatchBurst = false;
    private keyFrameCount = 0;
    private lastKeyFrame = 0;
    private lastKeyFrameTimeMs = 0;
    private totalBytes = 0;
    private encodeTimeHistory = new Denque<number>();
    private encodeStartTimes = new Denque<number>();
    private encodeQueueAtStart = new Denque<number>();
    // Parallel FIFO to encodeStartTimes; drives wire-side capturedAt+epoch on output.
    private capturedAtQueue = new Denque<MonotonicTime | undefined>();
    // Times sampled only when queue was 0 at start (actual codec cost).
    private pureEncodeTimeHistory = new Denque<number>();
    private chunkSequence = 0;
    // PLI diagnostics: set on forceKeyFrame submit, cleared on next 'key' output.
    private pendingForcedKfStartMs = 0;

    // Lifecycle counters surfaced via getStats() — distinguishes "healthy, waiting
    // for first frame" from "encoder died, pipeline silently retries".
    private reconfigureCount = 0;
    private replaceCount = 0;
    private lastReconfigureSummary = '';
    private lastReconfigureAtMs = 0;
    private lastErrorName = '';
    private lastErrorMessage = '';
    private lastErrorAtMs = 0;
    private errorCount = 0;

    // 0=base (lowest-res); 1+=higher-res. 0 for single-encoder pipelines (P2P).
    private readonly layerId: number;

    constructor(
    private config: EncoderConfig,
    private onChunk: (chunk: EncodedChunkData) => void,
    private onError: (error: Error) => void,
    layerId = 0,
    ) {
        this.layerId = layerId;
        this.encoder = this.createEncoder();
    }

    // Used by the encoder pool when adopting a pooled instance for a new pipeline:
    // updates config without recreating the VideoEncoder; subsequent initialize()
    // is a reconfigure (no NVENC re-init).
    setConfig(newConfig: EncoderConfig): void {
        this.config = newConfig;
    }

    getConfig(): Readonly<EncoderConfig> {
        return this.config;
    }

    initialize(): void {
        try {
            infoLog?.log(`Initializing: ${this.config.width}x${this.config.height} @ ${(this.config.bitrate / 1_000_000).toFixed(1)}Mbps`);
            const encoderConfig = this.buildEncoderConfig();
            this.encoder.configure(encoderConfig);
        } catch (error) {
            errorLog?.log('Failed to configure encoder:', error);
            this.recordError(error as Error);
            // Surface to onError so the dead-encoder fallback fires immediately.
            // DO NOT retry — every retry piles NVENC contention. Let the
            // codec-fallback chain handle it.
            this.onError(error as Error);
            throw error;
        }
    }

    encode(frame: VideoFrame, forceKeyFrame = false, capturedAt?: MonotonicTime): void {
        if (this.encoder.state !== 'configured') {
            this.droppedFrames++;
            const stateError = new DOMException(
                `Encoder state is '${this.encoder.state}'`, 'InvalidStateError');
            this.recordError(stateError);
            this.onError(stateError);
            frame.close();
            return;
        }

        // Rotation diagnostics: log encoder INPUT every ~10s. Distinguishes
        // downscaler-baked rotation (rotation=null/0, portrait dims match) from
        // encoder-tagged rotation (non-zero) — the latter makes Edge HEVC HW
        // disagree with Chrome on decoded display dims.
        if (this.frameCount === 0 || this.frameCount % 300 === 0) {
            const rot = (frame as VideoFrame & { rotation?: number | null }).rotation ?? null;
            infoLog?.log(
                `encode #${this.frameCount} (${this.config.codec}, layer=${this.layerId}): `
                + `display=${frame.displayWidth}x${frame.displayHeight} `
                + `coded=${frame.codedWidth}x${frame.codedHeight} `
                + `rotation=${rot ?? 'null'} `
                + `config=${this.config.width}x${this.config.height}`);
        }

        // Dims-mismatch guard: Chrome HW encoders silently top-left-crop when
        // frame.coded* exceeds configured dims (instead of scaling). This
        // happens transiently during reconfigure races between downscaler and
        // encoder configure() calls, which can't be applied atomically.
        // Dropping a few frames at the boundary is invisible; multi-second crop is not.
        if (frame.codedWidth !== this.config.width || frame.codedHeight !== this.config.height) {
            this.droppedFrames++;
            if (!this.inDimsMismatchBurst) {
                this.inDimsMismatchBurst = true;
                warnLog?.log(
                    `Encoder dims mismatch: frame=${frame.codedWidth}x${frame.codedHeight}, `
                    + `config=${this.config.width}x${this.config.height} — dropping frame(s) until match`);
            }
            frame.close();
            return;
        }
        this.inDimsMismatchBurst = false;

        this.encodeStartTimes.push(performance.now());
        this.encodeQueueAtStart.push(this.encoder.encodeQueueSize);
        this.capturedAtQueue.push(capturedAt);

        // Keyframe triggers: forceKeyFrame (PLI/pipeline), GOP frame count,
        // wall-clock floor (guarantees keyframe under slow capture so late
        // joiners can decode).
        const nowMs = performance.now();
        // Seed wall-clock baseline on first encode so the first keyframe is
        // also bounded — otherwise cap only kicks in after the first count-triggered KF.
        if (this.lastKeyFrameTimeMs === 0)
            this.lastKeyFrameTimeMs = nowMs;
        const shouldBeKeyFrame = forceKeyFrame
            || (this.frameCount - this.lastKeyFrame >= this.config.keyframeInterval)
            || (this.config.maxKeyFrameIntervalMs != null
                && nowMs - this.lastKeyFrameTimeMs >= this.config.maxKeyFrameIntervalMs);

        if (shouldBeKeyFrame) {
            this.lastKeyFrame = this.frameCount;
            this.lastKeyFrameTimeMs = nowMs;
        }

        if (forceKeyFrame) {
            this.pendingForcedKfStartMs = nowMs;
            infoLog?.log(
                `PLI: encode() forced KF requested (${this.config.codec}, layer=${this.layerId}, frame=${this.frameCount}, queue=${this.encoder.encodeQueueSize})`);
        }

        try {
            this.encoder.encode(frame, { keyFrame: shouldBeKeyFrame });
            this.frameCount++;
        } catch (error) {
            this.droppedFrames++;
            this.encodeStartTimes.pop();
            this.encodeQueueAtStart.pop();
            this.capturedAtQueue.pop();
            const e = error as Error;
            this.recordError(e);
            if (shouldLogEncoderError(`enc-throw:${e.name}:${e.message}`))
                errorLog?.log('Error encoding frame:', e.name, e.message);
            this.onError(e);
        } finally {
            frame.close();
        }
    }

    async flush(): Promise<void> {
        if (this.encoder.state === 'configured') {
            try {
                await this.encoder.flush();
            } catch (error) {
                errorLog?.log('Error flushing encoder:', error);
            }
        }
    }

    async reconfigure(params: { bitrate?: number; width?: number; height?: number }): Promise<void> {
        if (this.encoder.state !== 'configured') {
            throw new Error('Encoder is not configured');
        }

        const oldBitrate = this.config.bitrate;
        const oldWidth = this.config.width;
        const oldHeight = this.config.height;

        if (params.bitrate !== undefined) {
            this.config.bitrate = params.bitrate;
        }
        if (params.width !== undefined) {
            this.config.width = params.width;
        }
        if (params.height !== undefined) {
            this.config.height = params.height;
        }

        // Bump codec-string level if new dims cross threshold (e.g. 720p→1080p
        // forces AVC 3.1→4.0); else configure() throws NotSupportedError.
        const category = getCodecCategory(this.config.codec);
        const newCodec = getCodecForCategory(category, this.config.width, this.config.height);
        if (newCodec !== this.config.codec) {
            infoLog?.log(`Reconfigure codec string: ${this.config.codec} -> ${newCodec} (dims ${this.config.width}x${this.config.height} crosses level threshold)`);
            this.config.codec = newCodec;
        }
        const dimsChanged = oldWidth !== this.config.width || oldHeight !== this.config.height;
        this.reconfigureCount++;
        this.lastReconfigureSummary = `${oldBitrate / 1_000_000}Mbps ${oldWidth}x${oldHeight} -> ${this.config.bitrate / 1_000_000}Mbps ${this.config.width}x${this.config.height}`;
        this.lastReconfigureAtMs = performance.now();
        infoLog?.log(`Reconfigure: ${this.lastReconfigureSummary} (dimsChanged=${dimsChanged})`);

        if (dimsChanged) {
            // Android Chrome MediaCodec H.264 reproducibly fails with
            // OperationError when configure() is called in-place for a smaller
            // resolution mid-stream (queued old-dim frames hit the new config).
            // Replace the VideoEncoder entirely; bitrate-only stays in-place.
            await this.replaceUnderlyingEncoder();
            this.encoder.configure(this.buildEncoderConfig());
            this.lastKeyFrameTimeMs = 0;
        } else {
            this.encoder.configure(this.buildEncoderConfig());
        }
        // Force immediate keyframe so the new config is honored without
        // waiting for the next scheduled GOP boundary.
        this.lastKeyFrame = this.frameCount - this.config.keyframeInterval;
    }

    async switchCodec(newConfig: EncoderConfig): Promise<void> {
        await this.replaceUnderlyingEncoder();

        this.config = newConfig;

        // Reset to seq=0 — worker tears down the old videoStream, receivers see
        // a fresh stream. (reconfigure() preserves these; switchCodec doesn't.)
        this.reset();

        this.initialize();
        infoLog?.log(`Codec switched to ${newConfig.codec}`);
    }

    // Recovery: close + new VideoEncoder with same config. Called on first
    // OperationError so transient HW glitches don't blacklist the codec.
    // Caller owns cooldown/limit policy.
    async rebuild(): Promise<void> {
        await this.replaceUnderlyingEncoder();
        this.encoder.configure(this.buildEncoderConfig());
        // Counters preserved — this is recovery, not a stream restart.
        this.lastKeyFrame = this.frameCount - this.config.keyframeInterval;
        this.lastKeyFrameTimeMs = 0;
    }

    // Flush, close, await HW-slot release, then re-create. Caller must follow
    // up with configure()/initialize().
    private async replaceUnderlyingEncoder(): Promise<void> {
        if (this.encoder.state === 'configured') {
            try {
                await this.encoder.flush();
            } catch (error) {
                warnLog?.log('Flush before encoder rebuild failed (non-fatal):', error);
            }
            this.encoder.close();
        }
        await awaitHwReleased();
        this.encoder = this.createEncoder();
        this.replaceCount++;
        // Pending output callbacks on the closed session never fire — flush
        // their start-time entries so they don't pair with future outputs.
        this.encodeStartTimes.clear();
        this.encodeQueueAtStart.clear();
        this.capturedAtQueue.clear();
    }

    private createEncoder(): VideoEncoder {
        return new VideoEncoder({
            output: (chunk: EncodedVideoChunk, metadata?: EncodedVideoChunkMetadata) => {
                const startTime = this.encodeStartTimes.shift();
                const queueAtStart = this.encodeQueueAtStart.shift();
                const capturedAt = this.capturedAtQueue.shift();
                const encodeTime = startTime !== undefined
                    ? performance.now() - startTime
                    : 0;
                if (startTime !== undefined) {
                    this.encodeTimeHistory.push(encodeTime);
                    if (this.encodeTimeHistory.length > 100) {
                        this.encodeTimeHistory.shift();
                    }
                    if (queueAtStart === 0) {
                        this.pureEncodeTimeHistory.push(encodeTime);
                        if (this.pureEncodeTimeHistory.length > 100) {
                            this.pureEncodeTimeHistory.shift();
                        }
                    }
                }

                const chunkData: EncodedChunkData = {
                    chunk,
                    metadata,
                    timestamp: chunk.timestamp,
                    type: chunk.type,
                    byteLength: chunk.byteLength,
                    sequenceNumber: this.chunkSequence++,
                    encodeTimeMs: encodeTime,
                    layerId: this.layerId,
                    width: this.config.width,
                    height: this.config.height,
                    capturedAt,
                };

                this.totalBytes += chunk.byteLength;
                if (chunk.type === 'key') {
                    this.keyFrameCount++;
                    if (this.pendingForcedKfStartMs > 0) {
                        const elapsedMs = performance.now() - this.pendingForcedKfStartMs;
                        infoLog?.log(
                            `PLI: encoder OUTPUT forced KF in ${elapsedMs.toFixed(0)}ms ` +
                            `(${this.config.codec}, layer=${this.layerId}, bytes=${chunk.byteLength})`);
                        this.pendingForcedKfStartMs = 0;
                    }
                }

                // Rotation diagnostics: on each KF, parse decoderConfig (when
                // present) to detect bitstream-embedded rotation — that's what
                // makes Edge vs Chrome HEVC HW disagree on displayWidth/Height.
                if (chunk.type === 'key') {
                    const dc = metadata?.decoderConfig as (VideoDecoderConfig & {
                        codedWidth?: number; codedHeight?: number;
                        displayAspectWidth?: number; displayAspectHeight?: number;
                    }) | undefined;
                    if (dc) {
                        infoLog?.log(
                            `chunk KF (${this.config.codec}, layer=${this.layerId}): `
                            + `decoderConfig coded=${dc.codedWidth}x${dc.codedHeight} `
                            + `displayAspect=${dc.displayAspectWidth ?? 'n/a'}x${dc.displayAspectHeight ?? 'n/a'} `
                            + `descBytes=${dc.description ? (dc.description as ArrayBuffer | Uint8Array).byteLength : 'none'}`);
                    }
                }

                this.onChunk(chunkData);
            },
            error: (e: DOMException) => {
                this.recordError(e as unknown as Error);
                if (shouldLogEncoderError(`enc-cb:${e.name}:${e.message}`))
                    errorLog?.log('Encoder error:', e.name, e.message);
                this.onError(e as unknown as Error);
            }
        });
    }

    private buildEncoderConfig(): VideoEncoderConfig {
        const encoderConfig: VideoEncoderConfig = {
            codec: this.config.codec,
            width: this.config.width,
            height: this.config.height,
            bitrate: this.config.bitrate,
            framerate: this.config.framerate,
            latencyMode: this.config.latencyMode,
            hardwareAcceleration: this.config.hardwareAcceleration,
        };

        // Leave bitrateMode unset: variable is the spec default. Setting
        // explicit 'variable' + HEVC HW (Chrome) silently stalls the encoder.

        if (this.config.codec.startsWith('avc1')) {
            // Annex B everywhere: SPS/PPS embedded → no description, no AVCC overhead.
            encoderConfig.avc = { format: 'annexb' };
        } else if (this.config.codec.startsWith('hev1') || this.config.codec.startsWith('hvc1')) {
            (encoderConfig as VideoEncoderConfig & { hevc?: { format: string } }).hevc = { format: 'hevc' };
        }

        return encoderConfig;
    }

    close(): void {
        if (this.encoder.state !== 'closed') {
            this.encoder.close();
        }
    }

    getEncodeQueueSize(): number {
        return this.encoder.encodeQueueSize;
    }

    getState(): CodecState {
        return this.encoder.state;
    }

    getStats(): EncoderStats {
        const encodeHistory = this.encodeTimeHistory.toArray();
        const averageEncodeTime = encodeHistory.length > 0
            ? encodeHistory.reduce((a, b) => a + b, 0) / encodeHistory.length
            : 0;

        let medianEncodeTime = 0;
        if (encodeHistory.length > 0) {
            const sorted = encodeHistory.sort((a, b) => a - b);
            const mid = Math.floor(sorted.length / 2);
            medianEncodeTime = sorted.length % 2 !== 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        // Pure median: queue was empty at start — actual codec cost.
        let pureMedianEncodeTime = -1;
        const pureHistory = this.pureEncodeTimeHistory.toArray();
        if (pureHistory.length > 0) {
            const sorted = pureHistory.sort((a, b) => a - b);
            const mid = Math.floor(sorted.length / 2);
            pureMedianEncodeTime = sorted.length % 2 !== 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        // WebCodecs doesn't expose actual HW status; infer from config.
        let hardwareAcceleration = 'unknown';
        try {
            if (this.encoder.state === 'configured') {
                hardwareAcceleration = this.config.hardwareAcceleration === 'prefer-hardware'
                    ? 'likely (preferred)'
                    : 'software (preferred)';
            }
        } catch {
            hardwareAcceleration = 'unknown';
        }

        const nowMs = performance.now();
        return {
            encodedFrames: this.frameCount,
            droppedFrames: this.droppedFrames,
            keyFrames: this.keyFrameCount,
            totalBytes: this.totalBytes,
            averageEncodeTime,
            medianEncodeTime,
            pureMedianEncodeTime,
            configuredWidth: this.config.width,
            configuredHeight: this.config.height,
            configuredBitrate: this.config.bitrate,
            hardwareAcceleration,
            state: this.encoder.state,
            reconfigureCount: this.reconfigureCount,
            replaceCount: this.replaceCount,
            lastReconfigureSummary: this.lastReconfigureSummary,
            lastReconfigureAgeMs: this.lastReconfigureAtMs > 0 ? Math.round(nowMs - this.lastReconfigureAtMs) : -1,
            lastErrorName: this.lastErrorName,
            lastErrorMessage: this.lastErrorMessage,
            lastErrorAgeMs: this.lastErrorAtMs > 0 ? Math.round(nowMs - this.lastErrorAtMs) : -1,
            errorCount: this.errorCount,
        };
    }

    reset(): void {
        this.frameCount = 0;
        this.droppedFrames = 0;
        this.inDimsMismatchBurst = false;
        this.keyFrameCount = 0;
        this.lastKeyFrame = 0;
        this.lastKeyFrameTimeMs = 0;
        this.totalBytes = 0;
        this.encodeTimeHistory = new Denque<number>();
        this.encodeStartTimes = new Denque<number>();
        this.encodeQueueAtStart = new Denque<number>();
        this.pureEncodeTimeHistory = new Denque<number>();
        this.chunkSequence = 0;
        // Diagnostic counters intentionally NOT reset — they describe the
        // wrapper's lifetime, which the pool preserves across stop/start.
    }

    private recordError(error: Error): void {
        this.errorCount++;
        this.lastErrorName = error.name;
        this.lastErrorMessage = error.message;
        this.lastErrorAtMs = performance.now();
    }
}

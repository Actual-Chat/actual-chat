/**
 * WebCodecs Video Encoder
 * Encodes video frames to H.264 chunks with statistics tracking
 */

import { getLogs } from 'logging';
import Denque from 'denque';
import { getCodecCategory, getCodecForCategory } from './codec-support';

const { infoLog, warnLog, errorLog } = getLogs('VideoEncoder');

// Yield a macrotask so the platform can release a previous HW codec slot
// before allocating a new one. close() is synchronous; the slot is freed on
// the next task tick. Used between encoder close() → new VideoEncoder() to
// avoid OperationError 'Encoder creation error' on Chrome NVENC / VA-API.
async function awaitHwReleased(): Promise<void> {
    await Promise.resolve();
    await new Promise<void>(resolve => setTimeout(resolve, 0));
}

// Dedupe error logs by key within a 1 s window. Defends against async error
// cascades from the WebCodecs error callback when the platform fires errors
// faster than the dead-encoder watchdog can react.
const ERROR_LOG_DEDUPE_WINDOW_MS = 1000;
const errorLogLastSeenMs = new Map<string, number>();
function shouldLogEncoderError(key: string): boolean {
    const now = performance.now();
    const last = errorLogLastSeenMs.get(key) ?? 0;
    if (now - last < ERROR_LOG_DEDUPE_WINDOW_MS) return false;
    errorLogLastSeenMs.set(key, now);
    return true;
}

// WebCodecs SVC metadata (svc.temporalLayerId) is not yet in TS typings
function extractTemporalLayerId(metadata: EncodedVideoChunkMetadata | undefined): number | undefined {
    if (!metadata) return undefined;
    const svc = (metadata as Record<string, unknown>)['svc'];
    if (svc != null && typeof svc === 'object')
        return (svc as { temporalLayerId?: number }).temporalLayerId;
    return undefined;
}

export interface EncoderConfig {
  codec: string; // Support any codec string to handle H.264, HEVC, AV1, VP9, etc.
  width: number;
  height: number;
  bitrate: number;
  framerate: number;
  /**
   * Primary keyframe trigger — emit a keyframe every N encoded frames.
   * Encoder-natural knob (controls GOP size in frames for bandwidth planning).
   */
  keyframeInterval: number;
  /**
   * Wall-clock keyframe floor — guarantees a keyframe is emitted at least every
   * N ms regardless of input frame rate. Needed because `keyframeInterval` is
   * frame-count-based: if encoder.encode() is called slowly (VAD-reduced path,
   * static screencast with low capture framerate), the frame-count trigger can
   * drift to 10s+ wall time. Leaving this undefined keeps frame-count-only
   * behavior.
   */
  maxKeyFrameIntervalMs?: number;
  latencyMode: 'realtime' | 'quality';
  hardwareAcceleration: 'prefer-hardware' | 'prefer-software' | 'no-preference';
  scalabilityMode?: string; // Scalability mode like 'L1T1', 'L1T2', 'L1T3'
  /** Pre-convert frames to YUV before encoding. Disabled by default — HW encoders accept RGBA natively. */
  preConvertYuv?: boolean;
}

export interface EncodedChunkData {
  codec?: string; // Codec string (e.g., 'avc1.640028', 'av01.0.08M.08') — set by encoder worker
  chunk: EncodedVideoChunk;
  metadata: EncodedVideoChunkMetadata | undefined;
  timestamp: number;
  type: 'key' | 'delta';
  byteLength: number;
  sequenceNumber: number; // Added for chunk ordering to prevent out-of-order delivery issues
  encodeTimeMs: number;
  temporalLayerId?: number; // SVC temporal layer: 0 = base, 1+ = enhancement
  // Simulcast spatial layer: 0 = base (lowest-res) layer, 1+ = higher-res layers.
  // Always 0 for single-encoder (P2P) streams; set by encoder instance in multi-encoder mode.
  spatialLayerId?: number;
  // Encoded frame dimensions — the dims of the encoder instance that produced
  // this chunk. Needed so the worker can tag each layer's VideoStreamFrame with
  // its true resolution instead of borrowing from the primary encoder's config.
  width: number;
  height: number;
}

export interface EncoderStats {
  encodedFrames: number;
  droppedFrames: number;
  keyFrames: number;
  totalBytes: number;
  averageEncodeTime: number;
  medianEncodeTime: number;
  /** Encode time measured only when queue was empty (no wait component). -1 if no samples. */
  pureMedianEncodeTime: number;
  configuredWidth: number;
  configuredHeight: number;
  configuredBitrate: number;
  hardwareAcceleration: string;
  /** Current state of the underlying WebCodecs VideoEncoder. */
  state: CodecState;
  /** How many times reconfigure() was invoked on this WebCodecsEncoder. */
  reconfigureCount: number;
  /** How many times the underlying VideoEncoder instance has been replaced
   *  (close + new) — covers dim-change reconfigure and switchCodec paths. */
  replaceCount: number;
  /** Human-readable summary of the last reconfigure (e.g. "4Mbps 720x1280 -> 2Mbps 540x960"). */
  lastReconfigureSummary: string;
  /** Wall-clock ms since the last reconfigure() call. -1 if none yet. */
  lastReconfigureAgeMs: number;
  /** Last error reported by the encoder (sync throw or async `error` callback). */
  lastErrorName: string;
  lastErrorMessage: string;
  /** Wall-clock ms since the last error. -1 if none. */
  lastErrorAgeMs: number;
  /** Total errors observed from this WebCodecsEncoder (pre-dedupe). */
  errorCount: number;
}

export class WebCodecsEncoder {
    private encoder: VideoEncoder;
    private frameCount = 0;
    private droppedFrames = 0;
    // Latches when a dims-mismatch drop happens, clears on next accepted frame.
    // Coalesces the per-frame warn into one log per reconfigure-race burst.
    private inDimsMismatchBurst = false;
    private keyFrameCount = 0;
    private lastKeyFrame = 0;
    private lastKeyFrameTimeMs = 0;
    private totalBytes = 0;
    private encodeTimeHistory = new Denque<number>();
    private encodeStartTimes = new Denque<number>();
    private encodeQueueAtStart = new Denque<number>(); // Queue size when encode was called (parallel to encodeStartTimes)
    private pureEncodeTimeHistory = new Denque<number>(); // Times when queue was 0 at start (actual codec cost)
    private chunkSequence = 0; // Track chunk sequence for proper ordering

    // Diagnostics: track encoder lifecycle so the modal can distinguish
    // "encoder is healthy and waiting for first frame" from "encoder died,
    // pipeline silently keeps trying". Surfaced via getStats().
    private reconfigureCount = 0;
    private replaceCount = 0;
    private lastReconfigureSummary = '';
    private lastReconfigureAtMs = 0;
    private lastErrorName = '';
    private lastErrorMessage = '';
    private lastErrorAtMs = 0;
    private errorCount = 0;

    // Simulcast spatial layer ID this encoder instance is producing. 0 = base
    // (lowest-res) layer, 1+ = higher-res simulcast layers. Stamped onto every
    // chunk emitted by this instance. Defaults to 0 — single-encoder pipelines
    // (P2P, screencast, non-simulcast recordings) leave it at 0.
    private readonly spatialLayerId: number;

    constructor(
    private config: EncoderConfig,
    private onChunk: (chunk: EncodedChunkData) => void,
    private onError: (error: Error) => void,
    spatialLayerId = 0,
    ) {
        this.spatialLayerId = spatialLayerId;
        this.encoder = this.createEncoder();
    }

    // Replace the encoder's config (dims, bitrate, framerate, etc) without
    // recreating the underlying VideoEncoder instance. Used by the encoder pool
    // when a pooled instance is adopted by a new pipeline with different params
    // — initialize() is then called to apply the new config via VideoEncoder.configure(),
    // which on an existing 'configured' encoder is a reconfigure (no NVENC re-init).
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
            if (this.config.scalabilityMode) {
                infoLog?.log('Using scalability mode:', this.config.scalabilityMode);
            }
            this.encoder.configure(encoderConfig);
        } catch (error) {
            errorLog?.log('Failed to configure encoder:', error);
            this.recordError(error as Error);
            // Surface the synchronous configure() failure to onError so the
            // dead-encoder fallback fires immediately. (Async OperationError
            // from NVENC contention reaches onError via the encoder's `error`
            // callback.) DO NOT retry here — every retry creates a new NVENC
            // session attempt, piling on contention. Let the codec-fallback
            // chain do its thing.
            this.onError(error as Error);
            throw error;
        }
    }

    encode(frame: VideoFrame, forceKeyFrame = false): void {
        if (this.encoder.state !== 'configured') {
            this.droppedFrames++;
            const stateError = new DOMException(
                `Encoder state is '${this.encoder.state}'`, 'InvalidStateError');
            this.recordError(stateError);
            this.onError(stateError);
            frame.close();
            return;
        }

        // Dims-mismatch guard. Chrome's HW encoders (HEVC, H.264, AV1) silently
        // crop from the top-left corner when `frame.codedWidth/Height` exceeds
        // the configured dims instead of scaling — producing a visible
        // top-left-crop artifact on the receiver for what should be a
        // scaled-down frame. The mismatch happens transiently during a
        // reconfigure race: `downscaler.configure` and `encoder.configure`
        // can't be applied atomically, so a frame from the old downscaler
        // (old target dims) may reach a newly-reconfigured encoder, or
        // vice versa. Drop such frames — a few missed frames at a reconfigure
        // boundary are invisible; a multi-second crop is not.
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

        // Record start time and queue size for async timing measurement
        this.encodeStartTimes.push(performance.now());
        this.encodeQueueAtStart.push(this.encoder.encodeQueueSize);

        // Determine if this should be a keyframe
        // - forceKeyFrame: PLI from receiver or pipeline event
        // - frame-count trigger: encoder-natural GOP size
        // - wall-clock floor: guarantees a keyframe even if encoder is called slowly
        //   (VAD-reduced path, static screencast). Without this, retention can hold
        //   no keyframe and late-joining receivers can't start decoding.
        const nowMs = performance.now();
        // Seed the wall-clock baseline on first encode so the first keyframe is
        // also bounded by maxKeyFrameIntervalMs. Without this the cap only kicks
        // in after the first frame-count-triggered keyframe, leaving startup
        // unbounded when capture is slow.
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

        try {
            this.encoder.encode(frame, { keyFrame: shouldBeKeyFrame });
            this.frameCount++;
        } catch (error) {
            this.droppedFrames++;
            // Remove the start time and queue size since encode failed
            this.encodeStartTimes.pop();
            this.encodeQueueAtStart.pop();
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

        // Update config with new parameters
        if (params.bitrate !== undefined) {
            this.config.bitrate = params.bitrate;
        }
        if (params.width !== undefined) {
            this.config.width = params.width;
        }
        if (params.height !== undefined) {
            this.config.height = params.height;
        }

        // Bump codec string to a higher AVC/HEVC/VP9/AV1 level if the new dims
        // cross the level threshold (e.g. 720p → 1080p forces AVC 3.1 → 4.0).
        // Without this, encoder.configure() throws NotSupportedError because the
        // old codec string (e.g. avc1.64001F = level 3.1, max ~921k pixels) can't
        // describe a 1080p stream.
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
            // Android Chrome's MediaCodec H.264 session reproducibly fails with
            // OperationError "Encoding error." when configure() is called in-place
            // for a smaller resolution mid-stream (prod log 1777558634013:
            // avc1.4D4029, 720x1280 → 540x960 ✓ → 360x640 ✗). Likely cause:
            // queued old-dim frames hit the new MediaCodec config. Replace the
            // VideoEncoder instance entirely. Bitrate-only path below stays
            // in-place — that's reliable on every platform we see.
            await this.replaceUnderlyingEncoder();
            this.encoder.configure(this.buildEncoderConfig());
            // Restart the wall-clock keyframe floor so the new session emits a
            // fresh IDR immediately on the next encode.
            this.lastKeyFrameTimeMs = 0;
        } else {
            this.encoder.configure(this.buildEncoderConfig());
        }
        // Force immediate keyframe on next frame so the new config (dims or
        // bitrate target) is honored without waiting for the next scheduled
        // GOP boundary.
        this.lastKeyFrame = this.frameCount - this.config.keyframeInterval;
    }

    async switchCodec(newConfig: EncoderConfig): Promise<void> {
        await this.replaceUnderlyingEncoder();

        // Update config
        this.config = newConfig;

        // Reset counters to force immediate keyframe and restart sequencing —
        // worker tears down the old videoStream around switchCodec, so receivers
        // see this as a fresh stream starting at seq=0. (NB: reconfigure() does
        // NOT reset; it preserves chunkSequence/frameCount because the stream
        // continues unchanged.)
        this.reset();

        // Configure with new codec
        this.initialize();
        infoLog?.log(`Codec switched to ${newConfig.codec}`);
    }

    // Resurrect a dead encoder with the same config: close (if still open),
    // create a fresh VideoEncoder, and configure it. Called by the worker on
    // the first OperationError so transient HW glitches (MediaCodec session
    // hiccup, NVENC contention spike) don't blacklist the codec immediately.
    // Caller is responsible for cooldown/limit policy. Throws if configure()
    // fails synchronously — async errors after this returns will arrive via
    // the `error` callback as usual.
    async rebuild(): Promise<void> {
        await this.replaceUnderlyingEncoder();
        this.encoder.configure(this.buildEncoderConfig());
        // chunkSequence/frameCount/totalBytes preserved — this is a recovery,
        // not a stream restart. Force a fresh keyframe so receivers can resync
        // immediately after the gap caused by the dead session.
        this.lastKeyFrame = this.frameCount - this.config.keyframeInterval;
        this.lastKeyFrameTimeMs = 0;
    }

    // Flush in-flight encodes, close the current VideoEncoder session, wait
    // for the platform to release the HW slot, then create a fresh
    // VideoEncoder instance. Caller must follow up with configure() (or
    // initialize()) and any bookkeeping for stale per-encode state.
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
        // Output callbacks for any encodes still in flight on the closed
        // session never fire, so their start-time entries would otherwise sit
        // in the queues forever and pair with unrelated future outputs.
        this.encodeStartTimes.clear();
        this.encodeQueueAtStart.clear();
    }

    private createEncoder(): VideoEncoder {
        return new VideoEncoder({
            output: (chunk: EncodedVideoChunk, metadata?: EncodedVideoChunkMetadata) => {
                const startTime = this.encodeStartTimes.shift();
                const queueAtStart = this.encodeQueueAtStart.shift();
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
                    temporalLayerId: extractTemporalLayerId(metadata),
                    spatialLayerId: this.spatialLayerId,
                    width: this.config.width,
                    height: this.config.height,
                };

                this.totalBytes += chunk.byteLength;
                if (chunk.type === 'key') {
                    this.keyFrameCount++;
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

        // Variable bitrate is the WebCodecs spec default — leaving bitrateMode
        // unset gets variable mode without forcing a strict-VBR contract that
        // some HW encoders (Chrome HEVC HW + L1T2) silently reject by emitting
        // no output. Explicit 'variable' + HEVC HW = stuck encoder.

        if (this.config.scalabilityMode) {
            encoderConfig.scalabilityMode = this.config.scalabilityMode;
        }

        // Codec-specific config
        if (this.config.codec.startsWith('avc1')) {
            // Annex B everywhere: SPS/PPS embedded in bitstream → no description needed,
            // no AVCC serialization overhead, uniform across browsers.
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

        // Compute median encode time
        let medianEncodeTime = 0;
        if (encodeHistory.length > 0) {
            const sorted = encodeHistory.sort((a, b) => a - b);
            const mid = Math.floor(sorted.length / 2);
            medianEncodeTime = sorted.length % 2 !== 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        // Compute pure median encode time (queue was empty at start — no wait component)
        let pureMedianEncodeTime = -1;
        const pureHistory = this.pureEncodeTimeHistory.toArray();
        if (pureHistory.length > 0) {
            const sorted = pureHistory.sort((a, b) => a - b);
            const mid = Math.floor(sorted.length / 2);
            pureMedianEncodeTime = sorted.length % 2 !== 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        // Try to determine hardware acceleration status
        let hardwareAcceleration = 'unknown';
        try {
            // The encoder's decoderConfig property may contain hints about HW acceleration
            // Note: WebCodecs doesn't directly expose this, so we infer from configuration
            if (this.encoder.state === 'configured') {
                // If we requested hardware and encoder is working well, likely using it
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
        // lifetime of this WebCodecsEncoder instance, including switchCodec
        // boundaries. The pool keeps the same wrapper across stop/start.
    }

    private recordError(error: Error): void {
        this.errorCount++;
        this.lastErrorName = error.name;
        this.lastErrorMessage = error.message;
        this.lastErrorAtMs = performance.now();
    }
}

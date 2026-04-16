/**
 * WebCodecs Video Encoder
 * Encodes video frames to H.264 chunks with statistics tracking
 */

import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import Denque from 'denque';

const { infoLog, errorLog } = Log.get('VideoEncoder');

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
  temporalLayerId?: number; // SVC temporal layer: 0 = base, 1+ = enhancement
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
}

export class WebCodecsEncoder {
    private encoder: VideoEncoder;
    private frameCount = 0;
    private droppedFrames = 0;
    private keyFrameCount = 0;
    private lastKeyFrame = 0;
    private lastKeyFrameTimeMs = 0;
    private totalBytes = 0;
    private encodeTimeHistory = new Denque<number>();
    private encodeStartTimes = new Denque<number>();
    private encodeQueueAtStart = new Denque<number>(); // Queue size when encode was called (parallel to encodeStartTimes)
    private pureEncodeTimeHistory = new Denque<number>(); // Times when queue was 0 at start (actual codec cost)
    private chunkSequence = 0; // Track chunk sequence for proper ordering

    constructor(
    private config: EncoderConfig,
    private onChunk: (chunk: EncodedChunkData) => void,
    private onError: (error: Error) => void
    ) {
        this.encoder = this.createEncoder();
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
            throw error;
        }
    }

    encode(frame: VideoFrame, forceKeyFrame = false): void {
        if (this.encoder.state !== 'configured') {
            this.droppedFrames++;
            frame.close();
            return;
        }

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
            infoLog?.log(`Keyframe #${this.keyFrameCount + 1} at frame ${this.frameCount}`);
        }

        try {
            this.encoder.encode(frame, { keyFrame: shouldBeKeyFrame });
            this.frameCount++;
        } catch (error) {
            this.droppedFrames++;
            // Remove the start time and queue size since encode failed
            this.encodeStartTimes.pop();
            this.encodeQueueAtStart.pop();
            errorLog?.log('Error encoding frame:', error);
            this.onError(error as Error);
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

    // eslint-disable-next-line
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

        infoLog?.log(`Reconfigure: ${oldBitrate / 1_000_000}Mbps ${oldWidth}x${oldHeight} -> ${this.config.bitrate / 1_000_000}Mbps ${this.config.width}x${this.config.height}`);
        this.encoder.configure(this.buildEncoderConfig());

        // FIX: Force immediate keyframe on next frame by setting lastKeyFrame far enough in the past
        // This ensures the condition (frameCount - lastKeyFrame >= keyframeInterval) will be true on next encode
        this.lastKeyFrame = this.frameCount - this.config.keyframeInterval;
    }

    async switchCodec(newConfig: EncoderConfig): Promise<void> {
        // Flush and close existing encoder
        if (this.encoder.state === 'configured') {
            try {
                await this.encoder.flush();
            } catch (error) {
                errorLog?.log('Error flushing encoder during codec switch:', error);
            }
            this.encoder.close();
        }

        // Update config
        this.config = newConfig;

        // Reset counters to force immediate keyframe
        this.reset();

        // Create new encoder with same callbacks
        this.encoder = this.createEncoder();

        // Configure with new codec
        this.initialize();
        infoLog?.log(`Codec switched to ${newConfig.codec}`);
    }

    private createEncoder(): VideoEncoder {
        return new VideoEncoder({
            output: (chunk: EncodedVideoChunk, metadata?: EncodedVideoChunkMetadata) => {
                const startTime = this.encodeStartTimes.shift();
                const queueAtStart = this.encodeQueueAtStart.shift();
                if (startTime !== undefined) {
                    const encodeTime = performance.now() - startTime;
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
                    temporalLayerId: extractTemporalLayerId(metadata),
                };

                this.totalBytes += chunk.byteLength;
                if (chunk.type === 'key') {
                    this.keyFrameCount++;
                }

                this.onChunk(chunkData);
            },
            error: (e: DOMException) => {
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

        // Constant bitrate on iOS prevents CPU spikes on complex frames
        if (DeviceInfo.isIos) {
            encoderConfig.bitrateMode = 'constant';
        }

        if (this.config.scalabilityMode) {
            encoderConfig.scalabilityMode = this.config.scalabilityMode;
        }

        // Codec-specific config
        if (this.config.codec.startsWith('avc1')) {
            // Firefox produces Annex B even with 'avc' config (decode errors on other browsers).
            // iOS Safari's AVCC serialization adds ~150ms overhead per frame.
            // Use Annex B on both — SPS/PPS embedded in bitstream, no metadata overhead.
            const useAnnexB = DeviceInfo.isFirefox || DeviceInfo.isIos;
            encoderConfig.avc = { format: useAnnexB ? 'annexb' : 'avc' };
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
            hardwareAcceleration
        };
    }

    reset(): void {
        this.frameCount = 0;
        this.droppedFrames = 0;
        this.keyFrameCount = 0;
        this.lastKeyFrame = 0;
        this.lastKeyFrameTimeMs = 0;
        this.totalBytes = 0;
        this.encodeTimeHistory = new Denque<number>();
        this.encodeStartTimes = new Denque<number>();
        this.encodeQueueAtStart = new Denque<number>();
        this.pureEncodeTimeHistory = new Denque<number>();
        this.chunkSequence = 0;
    }
}

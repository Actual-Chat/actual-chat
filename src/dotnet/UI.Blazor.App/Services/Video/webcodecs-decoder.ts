/**
 * WebCodecs Video Decoder
 * Decodes H.264 chunks to video frames for real-time playback
 */

import type { EncodedChunkData } from './webcodecs-encoder';
import { getLogs } from 'logging';

const { infoLog, warnLog, errorLog } = getLogs('VideoDecoder');

// Drop non-key chunks when decoder queue exceeds this depth — prevents
// thrashing under network jitter where frames stack up faster than HW decode.
const BackpressureQueueLimit = 4;

export interface DecoderConfig {
  codec: string;
  optimizeForLatency: boolean;
  hardwareAcceleration: 'prefer-hardware' | 'prefer-software' | 'no-preference';
  description?: AllowSharedBufferSource;
  // Stream dimensions — passed into VideoDecoder.isConfigSupported probes so
  // codec candidates are validated against the real frame size, and re-probed
  // when a mid-stream layer switch carries a new description.
  codedWidth?: number;
  codedHeight?: number;
}

export interface DecoderStats {
  decodedFrames: number;
  droppedFrames: number;
  averageDecodeTime: number;
  medianDecodeTime: number;
  /** Decode time measured only when decode queue was empty (no wait component). -1 if no samples. */
  pureMedianDecodeTime: number;
  /** Current decode queue size from VideoDecoder API */
  decodeQueueSize: number;
  /** Number of delta frames dropped due to backpressure */
  backpressureDrops: number;
  /** Peak observed VideoDecoder.decodeQueueSize since last reset. */
  peakDecodeQueueSize: number;
  /** Duration of the most recent backpressure burst (ms from first drop to recovery keyframe). 0 if no burst observed yet. */
  lastArtifactWindowMs: number;
  /** Total number of backpressure-drop bursts observed since last reset. */
  artifactWindowsCount: number;
  hardwareAcceleration: string;
  resolution: string;
  // Pull stats — populated only when the decoder worker owns the pull loop
  // (off-thread pull). Main-thread pull tracks these in VideoPlayer directly.
  pullBitrateKbps?: number;
  pullReceivedBytes?: number;
  pullReceivedFrameCount?: number;
  pullReceivedKeyframeCount?: number;
  pullForwardedSpatialLayerId?: number;
  pullForwardedWidth?: number;
  pullForwardedHeight?: number;
  pullObservedMaxSpatialLayer?: number;
  // Encoded pre-decode buffer (the doc's `video buffer`). Lives in
  // decoder-worker.ts and is the receiver-side jitter absorber. Filled
  // by decoder-worker.getStats; the WebCodecsDecoder itself doesn't
  // populate these.
  encodedBufferDepth?: number;
  encodedBufferSpanMs?: number;
}

export class WebCodecsDecoder {
    private decoder: VideoDecoder;
    private frameCount = 0;
    private droppedFrames = 0;
    private decodeTimeHistory: number[] = [];
    private decodeStartTimes: number[] = [];
    private decodeQueueAtStart: number[] = []; // Queue depth when decode was called
    private pureDecodeTimeHistory: number[] = []; // Times when queue was 0 at start
    private lastResolution: { width: number; height: number } | null = null;
    private backpressureDrops = 0;
    private peakDecodeQueueSize = 0;
    private burstStartedAtMs = 0;
    private burstStartedSeq = -1;
    private burstDropCount = 0;
    private deltasPassedDuringBurst = 0;
    private dropDeltasUntilKeyframe = false;
    private lastArtifactWindowMs = 0;
    private artifactWindowsCount = 0;

    constructor(
    private config: DecoderConfig,
    /**
     * Called once per decoded frame. **Caller MUST `frame.close()` exactly once**
     * — either after rendering, after a postMessage transfer, or in an error
     * path. Without close, the platform GCs the frame and (in dev builds) logs
     * "VideoFrame was garbage-collected without being closed", and HW decoder
     * pool slots leak. Errors thrown synchronously here will propagate up
     * through `VideoDecoder.output` — wrap in try/finally if uncertain.
     */
    private onFrame: (frame: VideoFrame) => void,
    private onError: (error: Error) => void
    ) {
        this.decoder = new VideoDecoder({
            output: (frame: VideoFrame) => {
                // Track decode time - pop the start time and queue depth from queues
                const startTime = this.decodeStartTimes.shift();
                const queueAtStart = this.decodeQueueAtStart.shift();
                if (startTime !== undefined) {
                    const decodeTime = performance.now() - startTime;
                    this.decodeTimeHistory.push(decodeTime);
                    if (this.decodeTimeHistory.length > 100) {
                        this.decodeTimeHistory.shift();
                    }
                    // Pure decode time: only when queue was empty at decode start
                    if (queueAtStart === 0) {
                        this.pureDecodeTimeHistory.push(decodeTime);
                        if (this.pureDecodeTimeHistory.length > 100) {
                            this.pureDecodeTimeHistory.shift();
                        }
                    }
                }

                this.frameCount++;

                // Sample peak VideoDecoder queue depth so getStats() can show
                // whether we're routinely sitting near BackpressureQueueLimit
                // (the silent-delta-drop trigger). output() fires per decoded
                // frame, so this is amortized cost-free.
                const qSize = this.decoder.decodeQueueSize;
                if (qSize > this.peakDecodeQueueSize) this.peakDecodeQueueSize = qSize;

                // Track resolution changes
                const currentResolution = { width: frame.displayWidth, height: frame.displayHeight };
                if (!this.lastResolution ||
            this.lastResolution.width !== currentResolution.width ||
            this.lastResolution.height !== currentResolution.height) {
                    infoLog?.log(`Resolution changed: ${this.lastResolution ? `${this.lastResolution.width}x${this.lastResolution.height}` : 'initial'} -> ${currentResolution.width}x${currentResolution.height}`);
                    this.lastResolution = currentResolution;
                }

                this.onFrame(frame);
            },
            error: (e: DOMException) => {
                errorLog?.log('Decoder error:', e);
                this.onError(e as unknown as Error);
            }
        });
    }

    initialize(): void {
        try {
            const decoderConfig: VideoDecoderConfig = {
                codec: this.config.codec,
                optimizeForLatency: this.config.optimizeForLatency,
                hardwareAcceleration: this.config.hardwareAcceleration
            };

            // Add description if provided (required for AVC/H.264)
            if (this.config.description) {
                decoderConfig.description = this.config.description;
            }
            if (this.config.codedWidth) decoderConfig.codedWidth = this.config.codedWidth;
            if (this.config.codedHeight) decoderConfig.codedHeight = this.config.codedHeight;

            // Safari-specific optimizations for H.264 decoding
            const isSafari = /^((?!chrome|android).)*safari/i.test(navigator.userAgent);
            if (isSafari && this.config.codec.includes('avc1')) {
                infoLog?.log('Safari detected - applying H.264 optimizations');

                // Safari benefits from explicit hardware acceleration preference
                decoderConfig.hardwareAcceleration = 'prefer-hardware';

                // Safari performs better with latency optimization enabled
                decoderConfig.optimizeForLatency = true;
            }

            this.decoder.configure(decoderConfig);
            infoLog?.log('Decoder initialized:', this.decoder.state);
        } catch (error) {
            errorLog?.log('Failed to configure decoder:', error);
            throw error;
        }
    }

    /**
     * Reconfigure the decoder with a new description (typically a new keyframe's
     * SPS/PPS after a simulcast layer switch). When `codec` is provided and
     * differs from the current config codec, the decoder is reconfigured with
     * the new codec string too — this is required when the new keyframe's
     * description bytes carry a different tier/level/profile that changes the
     * derived codec string.
     *
     * `codedWidth` / `codedHeight` are required when the keyframe also carries
     * a new resolution (mid-stream layer switch / rotation). Without updating
     * them, Chromium's HEVC HW decoder keeps applying the *initial* SPS
     * conformance window and produces frames whose `displayWidth/Height` lag
     * the real picture — visible on Edge as a slight height shrink plus
     * stride-padding artifacts at the bottom of the frame.
     */
    updateDescription(
        description: AllowSharedBufferSource,
        codec?: string,
        codedWidth?: number,
        codedHeight?: number,
    ): void {
        if (this.decoder.state === 'closed') return;
        if (codec && codec !== this.config.codec) {
            infoLog?.log(`Decoder codec change: ${this.config.codec} -> ${codec}`);
            this.config = { ...this.config, codec };
        }
        if (codedWidth && codedWidth !== this.config.codedWidth) {
            this.config = { ...this.config, codedWidth };
        }
        if (codedHeight && codedHeight !== this.config.codedHeight) {
            this.config = { ...this.config, codedHeight };
        }
        this.decoder.configure({
            codec: this.config.codec,
            optimizeForLatency: this.config.optimizeForLatency,
            hardwareAcceleration: this.config.hardwareAcceleration,
            description,
            ...(this.config.codedWidth ? { codedWidth: this.config.codedWidth } : {}),
            ...(this.config.codedHeight ? { codedHeight: this.config.codedHeight } : {}),
        });
        this.decodeStartTimes = []; // flush stale timings — configure() aborts pending decodes
        this.decodeQueueAtStart = [];
        infoLog?.log('Decoder reconfigured with description');
    }

    decodeRaw(chunk: EncodedVideoChunk): void {
        if (this.decoder.state !== 'configured') {
            this.droppedFrames++;
            return;
        }
        if (this.dropDeltasUntilKeyframe) {
            if (chunk.type !== 'key') {
                this.droppedFrames++;
                return;
            }
            this.dropDeltasUntilKeyframe = false;
            this.closeArtifactWindow();
        }
        // Backpressure: drop delta chunks when decoder queue is saturated.
        // Always pass keyframes through so the decoder can resync.
        if (this.decoder.decodeQueueSize > BackpressureQueueLimit && chunk.type !== 'key') {
            this.backpressureDrops++;
            this.recordBackpressureDrop(chunk.timestamp);
            this.dropDeltasUntilKeyframe = true;
            return;
        }
        // Burst tracking: a keyframe terminates the artifact window; deltas
        // that pass the gate during a burst are counted to estimate how much
        // of the GOP actually decoded between first drop and recovery.
        if (this.burstDropCount > 0) {
            if (chunk.type === 'key') this.closeArtifactWindow();
            else this.deltasPassedDuringBurst++;
        }
        this.decodeStartTimes.push(performance.now());
        this.decodeQueueAtStart.push(this.decoder.decodeQueueSize);
        try {
            this.decoder.decode(chunk);
        } catch (error) {
            this.droppedFrames++;
            this.decodeStartTimes.pop();
            this.decodeQueueAtStart.pop();
            this.onError(error as Error);
        }
    }

    decode(chunkData: EncodedChunkData): void {
    // Check decoder state before attempting decode
        const currentState = this.decoder.state;

        if (currentState === 'closed') {
            this.droppedFrames++;
            errorLog?.log('Decoder is closed, cannot decode', chunkData.type, 'chunk');
            return;
        }

        if (currentState !== 'configured') {
            this.droppedFrames++;
            warnLog?.log(`Decoder not ready (state: ${currentState}), dropping ${chunkData.type} chunk`);
            return;
        }
        if (this.dropDeltasUntilKeyframe) {
            if (chunkData.type !== 'key') {
                this.droppedFrames++;
                return;
            }
            this.dropDeltasUntilKeyframe = false;
            this.closeArtifactWindow();
        }

        // Backpressure: drop delta chunks when decoder queue is saturated.
        if (this.decoder.decodeQueueSize > BackpressureQueueLimit && chunkData.type !== 'key') {
            this.backpressureDrops++;
            this.recordBackpressureDrop(chunkData.timestamp, chunkData.sequenceNumber);
            this.dropDeltasUntilKeyframe = true;
            return;
        }
        // Burst tracking: see decodeRaw() comment.
        if (this.burstDropCount > 0) {
            if (chunkData.type === 'key') this.closeArtifactWindow();
            else this.deltasPassedDuringBurst++;
        }

        // Record start time and queue depth for async timing measurement
        this.decodeStartTimes.push(performance.now());
        this.decodeQueueAtStart.push(this.decoder.decodeQueueSize);

        try {
            this.decoder.decode(chunkData.chunk);

            // Check state after decode to detect silent failures
            if (this.decoder.state === 'closed') {
                errorLog?.log('Decoder closed after decoding', chunkData.type, 'frame');
                this.onError(new Error(`Decoder closed unexpectedly after ${chunkData.type} frame - possible browser HEVC/AV1 bug`));
            }
        } catch (error) {
            this.droppedFrames++;
            // Remove the start time and queue depth since decode failed
            this.decodeStartTimes.pop();
            this.decodeQueueAtStart.pop();
            errorLog?.log(`Error decoding ${chunkData.type} chunk at timestamp ${chunkData.timestamp}:`, error);
            this.onError(error as Error);
        }
    }

    async flush(): Promise<void> {
        if (this.decoder.state === 'configured') {
            try {
                await this.decoder.flush();
            } catch (error) {
                errorLog?.log('Error flushing decoder:', error);
            }
        }
    }

    close(): void {
        if (this.decoder.state !== 'closed') {
            this.decoder.close();
        }
    }

    getState(): CodecState {
        return this.decoder.state;
    }

    /**
     * Current VideoDecoder.decodeQueueSize, or 0 if the decoder isn't
     * configured / safe to read. Used by the encoded pre-decode buffer's
     * drain loop to gate input rate so the decoded-frame backlog doesn't
     * grow unbounded inside the platform decoder.
     */
    getDecodeQueueSize(): number {
        try {
            if (this.decoder.state === 'configured')
                return this.decoder.decodeQueueSize;
        } catch { /* ignore */ }
        return 0;
    }

    getStats(): DecoderStats {
        const averageDecodeTime = this.decodeTimeHistory.length > 0
            ? this.decodeTimeHistory.reduce((a, b) => a + b, 0) / this.decodeTimeHistory.length
            : 0;

        // Compute median decode time
        let medianDecodeTime = 0;
        if (this.decodeTimeHistory.length > 0) {
            const sorted = [...this.decodeTimeHistory].sort((a, b) => a - b);
            const mid = Math.floor(sorted.length / 2);
            medianDecodeTime = sorted.length % 2 !== 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        // Compute pure median decode time (queue was empty at start — actual codec cost)
        let pureMedianDecodeTime = -1;
        if (this.pureDecodeTimeHistory.length > 0) {
            const sorted = [...this.pureDecodeTimeHistory].sort((a, b) => a - b);
            const mid = Math.floor(sorted.length / 2);
            pureMedianDecodeTime = sorted.length % 2 !== 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        // Try to determine hardware acceleration status
        let hardwareAcceleration = 'unknown';
        try {
            if (this.decoder.state === 'configured') {
                // If we requested hardware and decoder is working well, likely using it
                hardwareAcceleration = this.config.hardwareAcceleration === 'prefer-hardware'
                    ? 'likely (preferred)'
                    : this.config.hardwareAcceleration === 'no-preference'
                        ? 'auto (no-preference)'
                        : 'software (preferred)';
            }
        } catch {
            hardwareAcceleration = 'unknown';
        }

        // Format resolution string
        const resolution = this.lastResolution
            ? `${this.lastResolution.width}x${this.lastResolution.height}`
            : 'N/A';

        // Read queue size safely (decoder may be closed)
        let decodeQueueSize = 0;
        try {
            if (this.decoder.state === 'configured') {
                decodeQueueSize = this.decoder.decodeQueueSize;
            }
        } catch { /* ignore */ }

        return {
            decodedFrames: this.frameCount,
            droppedFrames: this.droppedFrames,
            averageDecodeTime,
            medianDecodeTime,
            pureMedianDecodeTime,
            decodeQueueSize,
            backpressureDrops: this.backpressureDrops,
            peakDecodeQueueSize: this.peakDecodeQueueSize,
            lastArtifactWindowMs: this.lastArtifactWindowMs,
            artifactWindowsCount: this.artifactWindowsCount,
            hardwareAcceleration,
            resolution
        };
    }

    reset(): void {
        this.frameCount = 0;
        this.droppedFrames = 0;
        this.backpressureDrops = 0;
        this.peakDecodeQueueSize = 0;
        this.burstStartedAtMs = 0;
        this.burstStartedSeq = -1;
        this.burstDropCount = 0;
        this.deltasPassedDuringBurst = 0;
        this.dropDeltasUntilKeyframe = false;
        this.lastArtifactWindowMs = 0;
        this.artifactWindowsCount = 0;
        this.decodeTimeHistory = [];
        this.decodeStartTimes = [];
        this.decodeQueueAtStart = [];
        this.pureDecodeTimeHistory = [];
    }

    private recordBackpressureDrop(mediaTimestamp: number, seq?: number): void {
        if (this.burstStartedAtMs === 0) {
            this.burstStartedAtMs = performance.now();
            this.burstStartedSeq = seq ?? -1;
            warnLog?.log(
                `Backpressure drop START: seq=${seq ?? '?'}, ` +
                `queueDepth=${this.decoder.decodeQueueSize}, ` +
                `mediaTs=${mediaTimestamp}us, ` +
                `peakQ=${this.peakDecodeQueueSize}`);
        }
        this.burstDropCount++;
    }

    private closeArtifactWindow(): void {
        if (this.burstStartedAtMs === 0)
            return;

        const windowMs = performance.now() - this.burstStartedAtMs;
        this.lastArtifactWindowMs = Math.round(windowMs);
        this.artifactWindowsCount++;
        warnLog?.log(
            `Backpressure drop END (recovery keyframe): ` +
            `dropsInBurst=${this.burstDropCount}, ` +
            `deltasPassed=${this.deltasPassedDuringBurst}, ` +
            `windowMs=${this.lastArtifactWindowMs}, ` +
            `firstDropSeq=${this.burstStartedSeq}, ` +
            `totalArtifactWindows=${this.artifactWindowsCount}`);
        this.burstStartedAtMs = 0;
        this.burstStartedSeq = -1;
        this.burstDropCount = 0;
        this.deltasPassedDuringBurst = 0;
    }
}

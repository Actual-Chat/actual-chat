import type { EncodedChunkData } from './webcodecs-encoder';
import { getLogs } from 'logging';

const { infoLog, warnLog, errorLog } = getLogs('VideoDecoder');

// Drop non-key chunks when decoder queue exceeds this depth.
const BackpressureQueueLimit = 4;

export interface DecoderConfig {
  codec: string;
  optimizeForLatency: boolean;
  hardwareAcceleration: 'prefer-hardware' | 'prefer-software' | 'no-preference';
  description?: AllowSharedBufferSource;
  // Stream dims — used by isConfigSupported probes; re-probed on mid-stream
  // layer switch carrying a new description.
  codedWidth?: number;
  codedHeight?: number;
}

export interface DecoderStats {
  decodedFrames: number;
  droppedFrames: number;
  averageDecodeTime: number;
  medianDecodeTime: number;
  // Sampled only when decode queue was empty at start; -1 if no samples.
  pureMedianDecodeTime: number;
  decodeQueueSize: number;
  backpressureDrops: number;
  peakDecodeQueueSize: number;
  // ms from first drop to recovery keyframe; 0 if no burst yet.
  lastArtifactWindowMs: number;
  artifactWindowsCount: number;
  hardwareAcceleration: string;
  resolution: string;
  // Populated only when the decoder worker owns the pull loop (off-thread pull).
  pullBitrateKbps?: number;
  pullReceivedBytes?: number;
  pullReceivedFrameCount?: number;
  pullReceivedKeyframeCount?: number;
  pullForwardedLayerId?: number;
  pullForwardedWidth?: number;
  pullForwardedHeight?: number;
  pullObservedMaxLayerId?: number;
  // Encoded pre-decode buffer (the receiver-side jitter absorber). Filled by
  // decoder-worker.getStats; not populated by WebCodecsDecoder itself.
  encodedBufferDepth?: number;
  encodedBufferSpanMs?: number;
}

export class WebCodecsDecoder {
    private decoder: VideoDecoder;
    private frameCount = 0;
    private droppedFrames = 0;
    private decodeTimeHistory: number[] = [];
    private decodeStartTimes: number[] = [];
    private decodeQueueAtStart: number[] = [];
    // Times sampled only when decoder queue was 0 at start.
    private pureDecodeTimeHistory: number[] = [];
    private lastResolution: { width: number; height: number } | null = null;
    private backpressureDrops = 0;
    private peakDecodeQueueSize = 0;
    private burstStartedAtMs = 0;
    private burstStartedSeq = -1;
    private burstDropCount = 0;
    private deltasPassedDuringBurst = 0;
    // After any chunk discard, later deltas may reference missing decoder state;
    // gate deltas until the next keyframe re-establishes the GOP.
    private dropDeltasUntilKeyframe = false;
    private lastArtifactWindowMs = 0;
    private artifactWindowsCount = 0;

    constructor(
    private config: DecoderConfig,
    // Caller MUST call frame.close() exactly once (after render, postMessage
    // transfer, or in error paths). Otherwise HW decoder pool slots leak and
    // dev builds log "VideoFrame was garbage-collected without being closed".
    private onFrame: (frame: VideoFrame) => void,
    private onError: (error: Error) => void
    ) {
        this.decoder = new VideoDecoder({
            output: (frame: VideoFrame) => {
                const startTime = this.decodeStartTimes.shift();
                const queueAtStart = this.decodeQueueAtStart.shift();
                if (startTime !== undefined) {
                    const decodeTime = performance.now() - startTime;
                    this.decodeTimeHistory.push(decodeTime);
                    if (this.decodeTimeHistory.length > 100) {
                        this.decodeTimeHistory.shift();
                    }
                    if (queueAtStart === 0) {
                        this.pureDecodeTimeHistory.push(decodeTime);
                        if (this.pureDecodeTimeHistory.length > 100) {
                            this.pureDecodeTimeHistory.shift();
                        }
                    }
                }

                this.frameCount++;

                const qSize = this.decoder.decodeQueueSize;
                if (qSize > this.peakDecodeQueueSize) this.peakDecodeQueueSize = qSize;

                // Log coded vs display dims on change — divergence means the
                // bitstream carried a transform (HEVC default_display_window,
                // SAR SEI). With the displayAspect=coded fix below, these
                // should match for portrait HEVC on every browser.
                const currentResolution = { width: frame.displayWidth, height: frame.displayHeight };
                if (!this.lastResolution ||
                    this.lastResolution.width !== currentResolution.width ||
                    this.lastResolution.height !== currentResolution.height) {
                    infoLog?.log(
                        `Resolution changed: ${this.lastResolution ? `${this.lastResolution.width}x${this.lastResolution.height}` : 'initial'} `
                        + `-> display=${currentResolution.width}x${currentResolution.height} `
                        + `coded=${frame.codedWidth}x${frame.codedHeight}`);
                    this.lastResolution = currentResolution;
                }

                this.onFrame(frame);
            },
            error: (e: DOMException) => {
                errorLog?.log('Decoder error:', e);
                this.gateAfterDroppedChunk();
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

            if (this.config.description) {
                decoderConfig.description = this.config.description;
            }
            if (this.config.codedWidth) decoderConfig.codedWidth = this.config.codedWidth;
            if (this.config.codedHeight) decoderConfig.codedHeight = this.config.codedHeight;
            // Force display=coded via 1:1 PAR — overrides any HEVC SPS
            // default_display_window or SAR SEI the bitstream may carry.
            // Without this, Edge's HEVC HW returns swapped portrait dims
            // (1280×720 for a 720×1280 coded stream) while Chrome agrees.
            if (this.config.codedWidth && this.config.codedHeight) {
                decoderConfig.displayAspectWidth = this.config.codedWidth;
                decoderConfig.displayAspectHeight = this.config.codedHeight;
            }

            const isSafari = /^((?!chrome|android).)*safari/i.test(navigator.userAgent);
            if (isSafari && this.config.codec.includes('avc1')) {
                infoLog?.log('Safari detected - applying H.264 optimizations');
                decoderConfig.hardwareAcceleration = 'prefer-hardware';
                decoderConfig.optimizeForLatency = true;
            }

            this.decoder.configure(decoderConfig);
            infoLog?.log(
                `Decoder configured: codec=${decoderConfig.codec} `
                + `coded=${decoderConfig.codedWidth ?? 'n/a'}x${decoderConfig.codedHeight ?? 'n/a'} `
                + `displayAspect=${decoderConfig.displayAspectWidth ?? 'n/a'}x${decoderConfig.displayAspectHeight ?? 'n/a'} `
                + `state=${this.decoder.state}`);
        } catch (error) {
            errorLog?.log('Failed to configure decoder:', error);
            throw error;
        }
    }

    // Reconfigure on layer switch. codedWidth/Height required when the keyframe
    // carries a new resolution — Chromium's HEVC HW keeps applying the initial
    // SPS conformance window otherwise (Edge: height shrink + bottom artifacts).
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
        // Carry displayAspect=coded forward on every reconfigure (see initialize).
        const aspect = this.config.codedWidth && this.config.codedHeight
            ? { displayAspectWidth: this.config.codedWidth, displayAspectHeight: this.config.codedHeight }
            : {};
        this.decoder.configure({
            codec: this.config.codec,
            optimizeForLatency: this.config.optimizeForLatency,
            hardwareAcceleration: this.config.hardwareAcceleration,
            description,
            ...(this.config.codedWidth ? { codedWidth: this.config.codedWidth } : {}),
            ...(this.config.codedHeight ? { codedHeight: this.config.codedHeight } : {}),
            ...aspect,
        });
        // configure() aborts pending decodes; flush stale timings to avoid mispairing.
        this.decodeStartTimes = [];
        this.decodeQueueAtStart = [];
        infoLog?.log(
            `Decoder reconfigured: codec=${this.config.codec} `
            + `coded=${this.config.codedWidth ?? 'n/a'}x${this.config.codedHeight ?? 'n/a'} `
            + `displayAspect=${this.config.codedWidth ?? 'n/a'}x${this.config.codedHeight ?? 'n/a'}`);
    }

    private dropDeltasGateArmedAtMs = 0;
    private dropDeltasGateDropCount = 0;

    private gateAfterDroppedChunk(): void {
        if (!this.dropDeltasUntilKeyframe) {
            this.dropDeltasGateArmedAtMs = performance.now();
            this.dropDeltasGateDropCount = 0;
            warnLog?.log(
                `dropDeltasUntilKeyframe armed: decodeQueueSize=${this.decoder.decodeQueueSize}, ` +
                `state=${this.decoder.state}`);
        }
        this.dropDeltasUntilKeyframe = true;
    }

    private clearDropDeltasGate(): void {
        if (this.dropDeltasUntilKeyframe) {
            const armedMs = performance.now() - this.dropDeltasGateArmedAtMs;
            warnLog?.log(
                `dropDeltasUntilKeyframe cleared: armed=${armedMs.toFixed(0)}ms, ` +
                `dropped=${this.dropDeltasGateDropCount} deltas`);
        }
        this.dropDeltasUntilKeyframe = false;
        this.dropDeltasGateDropCount = 0;
    }

    decodeRaw(chunk: EncodedVideoChunk): void {
        if (this.decoder.state !== 'configured') {
            this.droppedFrames++;
            this.gateAfterDroppedChunk();
            return;
        }
        if (this.dropDeltasUntilKeyframe) {
            if (chunk.type !== 'key') {
                this.droppedFrames++;
                this.dropDeltasGateDropCount++;
                return;
            }
            this.clearDropDeltasGate();
            this.closeArtifactWindow();
        }
        // Drop deltas under saturation; keyframes always pass for resync.
        if (this.decoder.decodeQueueSize > BackpressureQueueLimit && chunk.type !== 'key') {
            this.backpressureDrops++;
            this.recordBackpressureDrop(chunk.timestamp);
            this.gateAfterDroppedChunk();
            return;
        }
        // Burst tracking: keyframe terminates the artifact window; deltas that
        // pass during a burst estimate how much of the GOP decoded between
        // first drop and recovery.
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
            this.gateAfterDroppedChunk();
            this.onError(error as Error);
        }
    }

    decode(chunkData: EncodedChunkData): void {
        const currentState = this.decoder.state;

        if (currentState === 'closed') {
            this.droppedFrames++;
            errorLog?.log('Decoder is closed, cannot decode', chunkData.type, 'chunk');
            this.gateAfterDroppedChunk();
            return;
        }

        if (currentState !== 'configured') {
            this.droppedFrames++;
            warnLog?.log(`Decoder not ready (state: ${currentState}), dropping ${chunkData.type} chunk`);
            this.gateAfterDroppedChunk();
            return;
        }
        if (this.dropDeltasUntilKeyframe) {
            if (chunkData.type !== 'key') {
                this.droppedFrames++;
                this.dropDeltasGateDropCount++;
                return;
            }
            this.clearDropDeltasGate();
            this.closeArtifactWindow();
        }

        if (this.decoder.decodeQueueSize > BackpressureQueueLimit && chunkData.type !== 'key') {
            this.backpressureDrops++;
            this.recordBackpressureDrop(chunkData.timestamp, chunkData.sequenceNumber);
            this.gateAfterDroppedChunk();
            return;
        }
        if (this.burstDropCount > 0) {
            if (chunkData.type === 'key') this.closeArtifactWindow();
            else this.deltasPassedDuringBurst++;
        }

        this.decodeStartTimes.push(performance.now());
        this.decodeQueueAtStart.push(this.decoder.decodeQueueSize);

        try {
            this.decoder.decode(chunkData.chunk);

            // Detect silent failure: decoder closes itself on some HEVC/AV1 bugs.
            if (this.decoder.state === 'closed') {
                errorLog?.log('Decoder closed after decoding', chunkData.type, 'frame');
                this.onError(new Error(`Decoder closed unexpectedly after ${chunkData.type} frame - possible browser HEVC/AV1 bug`));
            }
        } catch (error) {
            this.droppedFrames++;
            this.decodeStartTimes.pop();
            this.decodeQueueAtStart.pop();
            this.gateAfterDroppedChunk();
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

    // Used by the pre-decode buffer's drain loop to gate input rate.
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

        let medianDecodeTime = 0;
        if (this.decodeTimeHistory.length > 0) {
            const sorted = [...this.decodeTimeHistory].sort((a, b) => a - b);
            const mid = Math.floor(sorted.length / 2);
            medianDecodeTime = sorted.length % 2 !== 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        // Pure decode time: queue was empty at start — actual codec cost.
        let pureMedianDecodeTime = -1;
        if (this.pureDecodeTimeHistory.length > 0) {
            const sorted = [...this.pureDecodeTimeHistory].sort((a, b) => a - b);
            const mid = Math.floor(sorted.length / 2);
            pureMedianDecodeTime = sorted.length % 2 !== 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        // WebCodecs doesn't expose actual HW status; infer from configured preference.
        let hardwareAcceleration = 'unknown';
        try {
            if (this.decoder.state === 'configured') {
                hardwareAcceleration = this.config.hardwareAcceleration === 'prefer-hardware'
                    ? 'likely (preferred)'
                    : this.config.hardwareAcceleration === 'no-preference'
                        ? 'auto (no-preference)'
                        : 'software (preferred)';
            }
        } catch {
            hardwareAcceleration = 'unknown';
        }

        const resolution = this.lastResolution
            ? `${this.lastResolution.width}x${this.lastResolution.height}`
            : 'N/A';

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

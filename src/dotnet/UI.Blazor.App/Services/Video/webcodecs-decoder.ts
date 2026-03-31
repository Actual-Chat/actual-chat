/**
 * WebCodecs Video Decoder
 * Decodes H.264 chunks to video frames for real-time playback
 */

import type { EncodedChunkData } from './webcodecs-encoder';
import { Log } from 'logging';

const { infoLog, warnLog, errorLog } = Log.get('VideoDecoder');

export interface DecoderConfig {
  codec: string;
  optimizeForLatency: boolean;
  hardwareAcceleration: 'prefer-hardware' | 'prefer-software' | 'no-preference';
  description?: AllowSharedBufferSource;
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
  hardwareAcceleration: string;
  resolution: string;
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

    constructor(
    private config: DecoderConfig,
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

    updateDescription(description: AllowSharedBufferSource): void {
    // Update decoder with new description (e.g., from encoder metadata)
        if (this.decoder.state !== 'closed') {
            this.decoder.configure({
                codec: this.config.codec,
                optimizeForLatency: this.config.optimizeForLatency,
                hardwareAcceleration: this.config.hardwareAcceleration,
                description
            });
            this.decodeStartTimes = []; // flush stale timings — configure() aborts pending decodes
            this.decodeQueueAtStart = [];
            infoLog?.log('Decoder reconfigured with description');
        }
    }

    decodeRaw(chunk: EncodedVideoChunk): void {
        if (this.decoder.state !== 'configured') {
            this.droppedFrames++;
            return;
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
            hardwareAcceleration,
            resolution
        };
    }

    reset(): void {
        this.frameCount = 0;
        this.droppedFrames = 0;
        this.backpressureDrops = 0;
        this.decodeTimeHistory = [];
        this.decodeStartTimes = [];
        this.decodeQueueAtStart = [];
        this.pureDecodeTimeHistory = [];
    }
}

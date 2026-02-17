/**
 * WebCodecs Video Decoder
 * Decodes H.264 chunks to video frames for real-time playback
 */

import type { EncodedChunkData } from './webcodecs-encoder';

export interface DecoderConfig {
  codec: string;
  optimizeForLatency: boolean;
  hardwareAcceleration: 'prefer-hardware' | 'prefer-software';
  description?: AllowSharedBufferSource;
}

export interface DecoderStats {
  decodedFrames: number;
  droppedFrames: number;
  averageDecodeTime: number;
  hardwareAcceleration: string;
  resolution: string;
}

export class WebCodecsDecoder {
    private decoder: VideoDecoder;
    private frameCount = 0;
    private droppedFrames = 0;
    private decodeTimeHistory: number[] = [];
    private decodeStartTimes: number[] = [];
    private lastResolution: { width: number; height: number } | null = null;

    constructor(
    private config: DecoderConfig,
    private onFrame: (frame: VideoFrame) => void,
    private onError: (error: Error) => void
    ) {
        this.decoder = new VideoDecoder({
            output: (frame: VideoFrame) => {
                // Track decode time - pop the start time from queue
                const startTime = this.decodeStartTimes.shift();
                if (startTime !== undefined) {
                    const decodeTime = performance.now() - startTime;
                    this.decodeTimeHistory.push(decodeTime);
                    if (this.decodeTimeHistory.length > 100) {
                        this.decodeTimeHistory.shift();
                    }
                }

                this.frameCount++;

                // Track resolution changes
                const currentResolution = { width: frame.displayWidth, height: frame.displayHeight };
                if (!this.lastResolution ||
            this.lastResolution.width !== currentResolution.width ||
            this.lastResolution.height !== currentResolution.height) {
                    console.log(`[Decoder] 📐 RESOLUTION CHANGED: ${this.lastResolution ? `${this.lastResolution.width}x${this.lastResolution.height}` : 'initial'} → ${currentResolution.width}x${currentResolution.height} (frame #${this.frameCount})`);
                    this.lastResolution = currentResolution;
                }

                this.onFrame(frame);
            },
            error: (e: DOMException) => {
                console.error('WebCodecs Decoder error:', e);
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
                console.log('[Decoder] Safari detected - applying H.264 specific optimizations');

                // Safari benefits from explicit hardware acceleration preference
                decoderConfig.hardwareAcceleration = 'prefer-hardware';

                // Safari performs better with latency optimization enabled
                decoderConfig.optimizeForLatency = true;
            }

            this.decoder.configure(decoderConfig);
            console.log('Decoder initialized', this.decoder.state);
        } catch (error) {
            console.error('Failed to configure decoder:', error);
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
            console.log('Decoder reconfigured with description');
        }
    }

    decode(chunkData: EncodedChunkData): void {
    // Check decoder state before attempting decode
        const currentState = this.decoder.state;

        if (currentState === 'closed') {
            this.droppedFrames++;
            console.error(`[Decoder] Decoder is closed, cannot decode ${chunkData.type} chunk. This may indicate a browser bug or unsupported codec configuration.`);
            return;
        }

        if (currentState !== 'configured') {
            this.droppedFrames++;
            console.warn(`[Decoder] Decoder not ready (state: ${currentState}), dropping ${chunkData.type} chunk`);
            return;
        }

        // Record start time for async timing measurement
        this.decodeStartTimes.push(performance.now());

        try {
            this.decoder.decode(chunkData.chunk);

            // Check state after decode to detect silent failures
            if (this.decoder.state === 'closed') {
                console.error(`[Decoder] Decoder closed after decoding ${chunkData.type} frame. This indicates a browser-level codec issue.`);
                this.onError(new Error(`Decoder closed unexpectedly after ${chunkData.type} frame - possible browser HEVC/AV1 bug`));
            }
        } catch (error) {
            this.droppedFrames++;
            // Remove the start time since decode failed
            this.decodeStartTimes.pop();
            console.error(`[Decoder] Error decoding ${chunkData.type} chunk at timestamp ${chunkData.timestamp}:`, error);
            console.error('[Decoder] State after error:', this.decoder.state);
            this.onError(error as Error);
        }
    }

    async flush(): Promise<void> {
        if (this.decoder.state === 'configured') {
            try {
                await this.decoder.flush();
            } catch (error) {
                console.error('Error flushing decoder:', error);
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

        // Try to determine hardware acceleration status
        let hardwareAcceleration = 'unknown';
        try {
            if (this.decoder.state === 'configured') {
                // If we requested hardware and decoder is working well, likely using it
                hardwareAcceleration = this.config.hardwareAcceleration === 'prefer-hardware'
                    ? 'likely (preferred)'
                    : 'software (preferred)';
            }
        } catch {
            hardwareAcceleration = 'unknown';
        }

        // Format resolution string
        const resolution = this.lastResolution
            ? `${this.lastResolution.width}x${this.lastResolution.height}`
            : 'N/A';

        return {
            decodedFrames: this.frameCount,
            droppedFrames: this.droppedFrames,
            averageDecodeTime,
            hardwareAcceleration,
            resolution
        };
    }

    reset(): void {
        this.frameCount = 0;
        this.droppedFrames = 0;
        this.decodeTimeHistory = [];
        this.decodeStartTimes = [];
    }
}

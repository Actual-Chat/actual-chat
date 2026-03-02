/**
 * Video Pipeline
 * Encode-only architecture: captures frames, encodes in worker, streams to server.
 * Decoding happens on the receiver side (video-player.ts uses its own decoder worker).
 *
 * Architecture:
 * - Encoder Worker (universal, RPC-based)
 * - Optional Segmentation Worker for background blur
 * - Canvas fallbacks for browsers without MSTP support
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';

import type { EncoderWorker } from '../workers/encoder-worker-contract';
import type { SegmentationWorker, SegmentationConfig, SegmentationStats, SegmentationWorkerCallbacks } from '../workers/segmentation-worker-contract';
import type { EncoderConfig, EncoderStats } from '../webcodecs-encoder';
import {
    VideoStreamer,
    type VideoStreamConfig,
    type VideoStreamFrame,
    microsecondsToTicks,
    VideoStream,
} from '../video-streamer';
import { Versioning } from 'versioning';
import { Log } from 'logging';
import { SessionTokens } from '../../../../UI.Blazor/Services/Security/session-tokens';
import type { Subscription } from 'rxjs';
import { RecorderStateHub } from '../../../Components/AudioRecorder/recorder-state-hub';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoPipeline');

export interface PipelineConfig {
  encoderConfig: EncoderConfig;
  /**
   * Background blur configuration (optional)
   * When enabled, frames are processed through segmentation before encoding
   */
  backgroundBlur?: {
    enabled: boolean;
    segmentationConfig: SegmentationConfig;
  };
  /**
   * Frame dropping configuration (optional)
   * When enabled, randomly drops frames during processing for testing
   * Default: false (no frames dropped)
   */
  frameDropping?: {
    enabled: boolean;
    dropProbability?: number; // Probability between 0 and 1 (default: 0.1 = 10% drop rate)
  };
  /**
   * Streaming configuration (optional)
   * When enabled, streams encoded chunks to server for real-time viewing
   */
  streaming?: {
    enabled: boolean;
    chatId: string;
  };
  /**
   * VAD-based adaptive framerate (optional)
   * When enabled, reduces framerate and bitrate when the sender is not speaking
   */
  adaptiveFramerate?: {
    enabled: boolean;
    /** Framerate when silent. Default: 5 */
    reducedFps?: number;
    /** Bitrate multiplier when silent (0-1). Default: 0.25 */
    reducedBitrateRatio?: number;
    /** Delay before reducing framerate after speech stops (ms). Default: 500 */
    silenceDelayMs?: number;
  };
}

// Type declarations for Insertable Streams API
declare class MediaStreamTrackProcessor<T = VideoFrame> {
    constructor(options: { track: MediaStreamTrack });
    readable: ReadableStream<T>;
}

export interface IVideoPipeline {
  start(inputStream: MediaStream): Promise<void>;
  stop(): Promise<void>;
  reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void>;
  switchCodec(newCodecString: string): Promise<void>;
  toggleBlur(enabled: boolean, segmentationConfig?: SegmentationConfig): Promise<void>;
  switchSegmentationBackend(backend: 'webgpu' | 'wasm'): Promise<void>;
  setPreviewCallback(callback: ((frame: VideoFrame) => void) | null): void;
  getEncoderStats(): EncoderStats;
  getSegmentationStats(): SegmentationStats | null;
}

export class VideoPipeline implements IVideoPipeline {
    private readonly encoderWorkerInstance: Worker;
    private readonly encoder: (EncoderWorker & Disposable);
    private segmentationWorkerInstance: Worker | null = null;
    private segmentationWorker: (SegmentationWorker & Disposable) | null = null;
    private processor: MediaStreamTrackProcessor | null = null;
    private frameReader: ReadableStreamDefaultReader<VideoFrame> | null = null;

    // Video streaming
    private videoStream: VideoStream | null = null; // VideoStream instance
    private pendingStreamFrames: VideoStreamFrame[] = []; // Buffer frames until we get codec description
    private codecSettings: string | null = null; // Base64 encoded codec description (SPS/PPS for H.264)
    private firstEncodedTimestamp: number | null = null; // First encoded chunk timestamp (microseconds) for 0-based normalization

    // Preview callback for rendering segmented frames before encoding
    private previewCallback: ((frame: VideoFrame) => void) | null = null;

    // Common
    private processing = false;

    // VAD-based adaptive framerate
    private remoteStreamCount = 0;
    private isSpeaking = true;
    private vadSubscription: Subscription | null = null;
    private vadSilenceTimer: ReturnType<typeof setTimeout> | null = null;
    private lastPassedFrameTime = 0;
    private readonly reducedFrameIntervalMs: number;
    private savedBitrate: number;

    private currentStats: {
    encoder: EncoderStats;
    segmentation: SegmentationStats | null;
  } = {
            encoder: {
                encodedFrames: 0,
                droppedFrames: 0,
                keyFrames: 0,
                totalBytes: 0,
                averageEncodeTime: 0,
                hardwareAcceleration: 'unknown'
            },
            segmentation: null
        };
    private statsInterval: number | null = null;

    private onSerializedChunk = (
        chunkBytes: ArrayBuffer,
        timestamp: number,
        duration: number,
        isKeyFrame: boolean,
        codec: string,
        sequenceNumber: number,
        descriptionBytes?: ArrayBuffer
    ) => {
        // Stream to server if enabled
        if (this.config.streaming?.enabled) {
            const chunkData = new Uint8Array(chunkBytes);
            // Normalize to 0-based offset so startedAtMs + offset gives correct epoch time
            this.firstEncodedTimestamp ??= timestamp; // microseconds
            const normalizedTimestamp = timestamp - this.firstEncodedTimestamp;

            const actualCodec = codec || this.config.encoderConfig.codec;
            if (isKeyFrame && codec && codec !== this.config.encoderConfig.codec) {
                warnLog?.log(`Encoder output codec (${codec}) differs from configured (${this.config.encoderConfig.codec}), updating config`);
                this.config.encoderConfig.codec = codec;
            }

            const frame: VideoStreamFrame = {
                offset: microsecondsToTicks(normalizedTimestamp),
                duration: microsecondsToTicks(duration),
                isKeyFrame,
                width: this.config.encoderConfig.width,
                height: this.config.encoderConfig.height,
                data: chunkData,
                codec: isKeyFrame ? actualCodec : undefined,
            };

            if (isKeyFrame) {
                const offsetMs = normalizedTimestamp / 1000;
                debugLog?.log(`Streaming keyframe: seq=${sequenceNumber}, offsetMs=${offsetMs.toFixed(0)}, ${(chunkData.length / 1024).toFixed(2)} KB`);
            }

            // Extract codec description from keyframes (required for H.264 decoder)
            if (isKeyFrame && descriptionBytes && descriptionBytes.byteLength > 0) {
                const descBytes = new Uint8Array(descriptionBytes);
                frame.description = descBytes;

                // If we don't have codecSettings yet, capture it from this keyframe
                if (!this.codecSettings) {
                    // Convert to base64 for transmission
                    let binary = '';
                    for (const byte of descBytes) {
                        binary += String.fromCharCode(byte);
                    }
                    this.codecSettings = btoa(binary);
                    debugLog?.log('Captured codec description:', descBytes.length, 'bytes,', this.codecSettings.length, 'base64 chars');
                }
            }

            // If videoStream doesn't exist yet, check if we can create it now
            if (!this.videoStream) {
                // AV1 doesn't produce a separate codec description (SPS/PPS) like H.264,
                // so we can create the stream on the first keyframe without codecSettings.
                const isAV1 = this.config.encoderConfig.codec.startsWith('av01');
                const canCreateStream = this.codecSettings ?? (isAV1 && isKeyFrame);

                if (canCreateStream) {
                    // We have what we need — create the stream now
                    const settings = this.codecSettings ?? '';
                    infoLog?.log(`Creating VideoStream with codecSettings (${settings.length} chars), isAV1=${isAV1}`);
                    const streamConfig: VideoStreamConfig = {
                        codec: this.config.encoderConfig.codec,
                        width: this.config.encoderConfig.width,
                        height: this.config.encoderConfig.height,
                        codecSettings: settings,
                    };
                    const sessionToken = SessionTokens.current;
                    this.videoStream = VideoStreamer.addStream(
                        sessionToken,
                        this.config.streaming.chatId,
                        streamConfig
                    );
                    infoLog?.log(`VideoStream created, sending ${this.pendingStreamFrames.length} buffered frames`);

                    // Send all buffered frames
                    for (const bufferedFrame of this.pendingStreamFrames) {
                        this.videoStream.addFrame(bufferedFrame);
                    }
                    this.pendingStreamFrames = [];

                    // Send the current frame
                    this.videoStream.addFrame(frame);
                } else {
                    // Buffer the frame until we get the codec description (H.264)
                    this.pendingStreamFrames.push(frame);
                }
            } else {
                // Stream exists, send the frame directly
                this.videoStream.addFrame(frame);
            }
        }
    };

    private onSegmentationFrameProcessed = async (frame: VideoFrame, _sequenceNumber: number, _processingTime: number) => {
        // Render to preview canvas before transferring to encoder
        // (drawImage reads the frame without consuming it; encodeFrame transfers it)
        if (this.previewCallback) {
            try {
                this.previewCallback(frame);
            } catch (error) {
                errorLog?.log('Preview callback error:', error);
            }
        }

        // Send processed frame to encoder
        try {
            await this.encoder.encodeFrame(frame);
        } catch {
            // Close frame if RPC transfer failed (e.g., encoder shutting down)
            try { frame.close(); } catch { /* already transferred/closed */ }
        }
    };

    private onSegmentationError = (error: Error) => {
        errorLog?.log('Segmentation error:', error.message);
    // Could implement fallback logic here (e.g., disable blur and continue without it)
    };

    constructor(private config: PipelineConfig) {
    // Compute adaptive framerate settings
        const af = config.adaptiveFramerate;
        this.reducedFrameIntervalMs = 1000 / (af?.reducedFps ?? 5);
        this.savedBitrate = config.encoderConfig.bitrate;

        // Create encoder worker instance
        const encoderWorkerPath = Versioning.mapPath('/dist/videoEncoderWorker.js');
        infoLog?.log('Creating encoder worker from:', encoderWorkerPath);
        this.encoderWorkerInstance = new Worker(
            encoderWorkerPath,
            { type: 'module' }
        );
        this.encoderWorkerInstance.onerror = (e) => errorLog?.log('Encoder worker error:', e);

        // Create RPC proxy
        this.encoder = rpcClientServer<EncoderWorker>(
            'VideoPipeline.encoder',
            this.encoderWorkerInstance,
            { onSerializedChunk: this.onSerializedChunk }
        );

        // Initialize segmentation worker if background blur is enabled
        if (this.config.backgroundBlur?.enabled) {
            infoLog?.log('Creating segmentation worker for background blur with config:', this.config.backgroundBlur);
            const segmentationWorkerPath = Versioning.mapPath('/dist/videoSegmentationWorker.js');
            this.segmentationWorkerInstance = new Worker(
                segmentationWorkerPath,
                { type: 'module' }
            );

            this.segmentationWorker = rpcClientServer<SegmentationWorker>(
                'VideoPipeline.segmentation',
                this.segmentationWorkerInstance,
        {
            onFrameProcessed: (frame: VideoFrame, seq: number, time: number) => { void this.onSegmentationFrameProcessed(frame, seq, time); },
            onError: this.onSegmentationError
        } as SegmentationWorkerCallbacks
            );
            infoLog?.log('Segmentation worker created and RPC proxy initialized');
        } else {
            infoLog?.log('Background blur not enabled in config:', this.config.backgroundBlur);
        }
    }

    public async start(inputStream: MediaStream): Promise<void> {
        infoLog?.log('Starting video pipeline...');

        // Initialize video streaming if enabled (stream will be created when first keyframe with description arrives)
        if (this.config.streaming?.enabled) {
            infoLog?.log('Initializing video streaming to server (will wait for first keyframe with codec description)');

            // Initialize VideoStreamer SignalR connection
            const hubUrl = new URL('/api/hub/streams', window.location.origin).toString();
            VideoStreamer.init(hubUrl);

            // VideoStream will be created when first keyframe with description arrives
            // This ensures we can pass codecSettings to the server
            infoLog?.log('Video streaming SignalR initialized, waiting for first keyframe');
        }

        // Get input video track
        const videoTrack = inputStream.getVideoTracks()[0];

        // IMPORTANT: Set processing to true BEFORE creating frame extractor
        // This fixes a race condition in Safari where the canvas fallback's pump()
        // function closes the stream immediately if processing is false
        this.processing = true;

        // Create processor to extract frames (with canvas fallback for older Safari)
        const hasMSTP = this.hasMSTPInWindow();
        infoLog?.log(`MSTP available: ${hasMSTP}`);

        if (hasMSTP) {
            try {
                this.processor = new MediaStreamTrackProcessor({ track: videoTrack });
                this.frameReader = this.processor.readable.getReader();
                debugLog?.log('Using MSTP for frame extraction');
            } catch (error) {
                errorLog?.log('MSTP creation failed, falling back to canvas:', error);
                this.frameReader = this.createCanvasFrameExtractor(videoTrack);
            }
        } else {
            // Canvas-based fallback for older browsers
            infoLog?.log('MSTP not available - using canvas-based frame extraction fallback');
            this.frameReader = this.createCanvasFrameExtractor(videoTrack);
        }

        // Initialize encoder worker and wait for it to be ready
        const initPromises: Promise<void>[] = [
            this.encoder.initialize(this.config.encoderConfig, { type: 'rpc-timeout', timeoutMs: 5000 }),
        ];

        // Initialize segmentation worker if background blur is enabled
        if (this.segmentationWorker && this.config.backgroundBlur?.enabled) {
            initPromises.push(
                this.segmentationWorker.initialize(
                    this.config.backgroundBlur.segmentationConfig,
                    { timeoutMs: 10000 } // Longer timeout for model loading
                ).catch((error: unknown) => {
                    errorLog?.log('Failed to initialize segmentation worker:', error);
                    throw error;
                })
            );
            infoLog?.log('Initializing segmentation worker for background blur');
        }

        await Promise.all(initPromises);
        infoLog?.log('Encoder worker ready via RPC');

        // Start pumping frames to encoder worker
        // Note: this.processing was already set to true earlier (before frame extractor creation)
        void this.pumpFrames();

        // Start stats polling via RPC
        this.statsInterval = window.setInterval(() => {
            void (async () => {
                const encoderStats = await this.encoder.getStats();
                this.currentStats.encoder = encoderStats;
                if (this.segmentationWorker) {
                    try {
                        const segStats = await this.segmentationWorker.getStats();
                        this.currentStats.segmentation = segStats;
                    } catch (error) {
                        warnLog?.log('Failed to get segmentation stats:', error);
                    }
                }
            })();
        }, 1000);

        infoLog?.log('Pipeline started: Encoder Worker → Server streaming');
    }

    public async stop(): Promise<void> {
        infoLog?.log('Stopping pipeline...');

        // Unsubscribe from VAD
        if (this.vadSubscription) {
            this.vadSubscription.unsubscribe();
            this.vadSubscription = null;
        }
        if (this.vadSilenceTimer !== null) {
            clearTimeout(this.vadSilenceTimer);
            this.vadSilenceTimer = null;
        }
        this.isSpeaking = true;

        // Stop stats polling
        if (this.statsInterval) {
            clearInterval(this.statsInterval);
            this.statsInterval = null;
        }

        // Stop pumping frames
        this.processing = false;

        // Cancel frame reader
        if (this.frameReader) {
            try {
                await this.frameReader.cancel();
            } catch (e: unknown) {
                warnLog?.log('Frame reader cancel error:', e);
            }
            this.frameReader = null;
        }

        // Stop encoder worker via RPC
        await this.encoder.stop();
        infoLog?.log('Encoder stopped via RPC');

        // Stop segmentation worker if it exists
        if (this.segmentationWorker) {
            try {
                await this.segmentationWorker.stop();
                infoLog?.log('Segmentation worker stopped via RPC');
            } catch (error) {
                warnLog?.log('Error stopping segmentation worker:', error);
            }
        }

        // Complete video stream if active
        if (this.videoStream) {
            this.videoStream.complete();
            infoLog?.log('Video stream completed');
            this.videoStream = null;
        }

        // Reset timestamp normalization
        this.firstEncodedTimestamp = null;

        // Cleanup RPC clients and worker instances
        this.encoder.dispose();
        this.encoderWorkerInstance.terminate();

        if (this.segmentationWorker) {
            this.segmentationWorker.dispose();
            this.segmentationWorker = null;
        }
        if (this.segmentationWorkerInstance) {
            this.segmentationWorkerInstance.terminate();
            this.segmentationWorkerInstance = null;
        }

        infoLog?.log('Pipeline stopped with RPC cleanup');
    }


    /**
   * Dynamically reconfigure encoder with new bitrate and/or resolution
   */
    async reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void> {
        infoLog?.log(`Reconfiguring via RPC: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);

        // Update config
        this.config.encoderConfig.bitrate = params.bitrate;
        this.config.encoderConfig.width = params.width;
        this.config.encoderConfig.height = params.height;

        // Track base bitrate for VAD-based reduction
        this.savedBitrate = params.bitrate;

        // If currently in VAD silence, apply reduced ratio to new base bitrate
        if (!this.isSpeaking && this.config.adaptiveFramerate?.enabled) {
            const ratio = this.config.adaptiveFramerate.reducedBitrateRatio ?? 0.25;
            const reducedBitrate = Math.round(params.bitrate * ratio);
            debugLog?.log(`reconfigure during silence: applying reduced bitrate ${reducedBitrate}`);
            await this.encoder.reconfigure({ ...params, bitrate: reducedBitrate });
            return;
        }

        await this.encoder.reconfigure(params);
    }

    /**
   * Switch codec mid-stream: complete current VideoStream, reset encoder, start new stream
   */
    async switchCodec(newCodecString: string): Promise<void> {
        if (newCodecString === this.config.encoderConfig.codec) {
            infoLog?.log(`switchCodec: already using ${newCodecString}, skipping`);
            return;
        }

        infoLog?.log(`Switching codec from ${this.config.encoderConfig.codec} to ${newCodecString}`);

        // 1. Complete current video stream so the server-side stream ends
        if (this.videoStream) {
            this.videoStream.complete();
            infoLog?.log('Current video stream completed');
            this.videoStream = null;
        }

        // 2. Reset streaming state so new encoded frames trigger a new VideoStream
        this.codecSettings = null;
        this.firstEncodedTimestamp = null;
        this.pendingStreamFrames = [];

        // 3. Build new encoder config with the new codec
        const newEncoderConfig: EncoderConfig = {
            ...this.config.encoderConfig,
            codec: newCodecString,
        };

        // Add codec-specific format (avc needs { format: 'avc' })
        // This is handled inside the encoder's initialize(), but we update our local config
        this.config.encoderConfig = newEncoderConfig;

        // 4. Switch encoder in worker (flush + close old, create + configure new)
        await this.encoder.switchCodec(newEncoderConfig);

        infoLog?.log(`Codec switched to ${newCodecString}`);
    }

    /**
   * Dynamically toggle background blur on/off during recording
   */
    async toggleBlur(enabled: boolean, segmentationConfig?: SegmentationConfig): Promise<void> {
        infoLog?.log(`Toggling background blur: ${enabled ? 'ON' : 'OFF'}`);

        if (enabled && !this.segmentationWorker) {
            // Initialize segmentation worker if enabling blur and it doesn't exist
            if (!this.config.backgroundBlur && !segmentationConfig) {
                throw new Error('Cannot enable blur: background blur not configured and no segmentation config provided');
            }

            // Set the config if provided
            if (!this.config.backgroundBlur && segmentationConfig) {
                this.config.backgroundBlur = {
                    enabled: true,
                    segmentationConfig
                };
            }

            if (!this.config.backgroundBlur) {
                throw new Error('Cannot enable blur: background blur not configured');
            }

            infoLog?.log('Initializing segmentation worker for dynamic blur enable...');

            const segmentationWorkerPath = Versioning.mapPath('/dist/videoSegmentationWorker.js');
            this.segmentationWorkerInstance = new Worker(
                segmentationWorkerPath,
                { type: 'module' }
            );

            this.segmentationWorker = rpcClientServer<SegmentationWorker>(
                'VideoPipeline.segmentation',
                this.segmentationWorkerInstance,
                {
                    onFrameProcessed: this.onSegmentationFrameProcessed,
                    onError: this.onSegmentationError
                }
            );

            // Initialize the worker
            await this.segmentationWorker.initialize(
                this.config.backgroundBlur.segmentationConfig,
                { timeoutMs: 10000 }
            );

            infoLog?.log('Segmentation worker initialized for dynamic blur toggle');
        }

        if (this.config.backgroundBlur) {
            this.config.backgroundBlur.enabled = enabled;
        }

        // Update segmentation worker config to enable/disable blur
        if (this.segmentationWorker) {
            await this.segmentationWorker.updateConfig({ blurEnabled: enabled });
            infoLog?.log(`Updated segmentation worker blurEnabled to ${enabled}`);
        }

        infoLog?.log(`Background blur ${enabled ? 'enabled' : 'disabled'}`);
    }

    getEncoderStats(): EncoderStats {
        return { ...this.currentStats.encoder };
    }

    getSegmentationStats(): SegmentationStats | null {
        return this.currentStats.segmentation ? { ...this.currentStats.segmentation } : null;
    }

    /**
   * Set a callback to receive processed (blurred) frames for local preview.
   * The callback is invoked before the frame is transferred to the encoder,
   * so drawImage/canvas operations are safe.
   */
    setPreviewCallback(callback: ((frame: VideoFrame) => void) | null): void {
        this.previewCallback = callback;
    }

    /**
   * Update segmentation configuration dynamically during recording
   */
    async updateSegmentationConfig(config: Partial<SegmentationConfig>): Promise<void> {
        infoLog?.log('Updating segmentation config:', config);

        if (this.segmentationWorker) {
            // Update worker config via RPC
            await this.segmentationWorker.updateConfig(config);
        }

        // Update local config if it exists
        if (this.config.backgroundBlur?.segmentationConfig) {
            this.config.backgroundBlur.segmentationConfig = {
                ...this.config.backgroundBlur.segmentationConfig,
                ...config
            };
        }
    }

    /**
   * Switch the segmentation backend dynamically during recording
   * Recreates the segmentation worker with the new backend configuration
   */
    async switchSegmentationBackend(newBackend: 'webgpu' | 'wasm'): Promise<void> {
        infoLog?.log(`Switching segmentation backend to: ${newBackend}`);

        if (!this.segmentationWorker || !this.config.backgroundBlur) {
            throw new Error('Segmentation worker not available or background blur not enabled');
        }

        // Stop and dispose current segmentation worker
        try {
            await this.segmentationWorker.stop();
            this.segmentationWorker.dispose();
            if (this.segmentationWorkerInstance) {
                this.segmentationWorkerInstance.terminate();
            }
        } catch (error) {
            warnLog?.log('Error stopping current segmentation worker:', error);
        }

        // Update config with new backend
        const currentConfig = this.config.backgroundBlur.segmentationConfig;
        const updatedConfig: SegmentationConfig = {
            ...currentConfig,
            backend: newBackend,
        };

        // Recreate worker instance
        const segmentationWorkerPath = Versioning.mapPath('/dist/videoSegmentationWorker.js');
        this.segmentationWorkerInstance = new Worker(
            segmentationWorkerPath,
            { type: 'module' }
        );

        // Recreate RPC proxy
        this.segmentationWorker = rpcClientServer<SegmentationWorker>(
            'VideoPipeline.segmentation',
            this.segmentationWorkerInstance,
      {
          onFrameProcessed: (frame: VideoFrame, seq: number, time: number) => { void this.onSegmentationFrameProcessed(frame, seq, time); },
          onError: this.onSegmentationError
      } as SegmentationWorkerCallbacks
        );

        // Reinitialize with updated config
        await this.segmentationWorker.initialize(updatedConfig, { timeoutMs: 10000 });

        // Update local config
        this.config.backgroundBlur.segmentationConfig = updatedConfig;

        infoLog?.log(`Successfully switched segmentation backend to ${newBackend}`);
    }

    /**
   * Update frame dropping configuration dynamically during recording
   */
    updateFrameDroppingConfig(enabled: boolean, dropProbability = 0.1): void {
        infoLog?.log(`Updating frame dropping config: enabled=${enabled}, probability=${dropProbability}`);

        // Create or update local config
        this.config.frameDropping = {
            enabled,
            dropProbability
        };

        infoLog?.log('Frame dropping config now:', this.config.frameDropping);
    }

    /**
     * Subscribe to audio VAD state to drive adaptive framerate.
     * Call after pipeline is started.
     */
    subscribeToVad(): void {
        if (this.vadSubscription) return;

        this.vadSubscription = RecorderStateHub.recorderStateChanged$.subscribe(state => {
            // When audio is not recording, treat as speaking (don't reduce framerate)
            const active = !state.isRecording || state.isVoiceActive;
            this.setVadActive(active);
        });

        // Sync with current state
        const current = RecorderStateHub.getState();
        this.setVadActive(!current.isRecording || current.isVoiceActive);

        debugLog?.log('Subscribed to VAD for adaptive framerate');
    }

    /**
     * Update the remote stream count for slowdown decisions.
     * Slowdown only applies in group calls (3+ total streams = 2+ remote).
     */
    setRemoteStreamCount(count: number): void {
        const wasGroup = this.remoteStreamCount >= 2;
        this.remoteStreamCount = count;
        const isGroup = count >= 2;
        debugLog?.log('setRemoteStreamCount:', count);

        // Transitioning from group → non-group: cancel pending slowdown & restore
        if (wasGroup && !isGroup) {
            if (this.vadSilenceTimer !== null) {
                clearTimeout(this.vadSilenceTimer);
                this.vadSilenceTimer = null;
            }
            if (!this.isSpeaking) {
                this.isSpeaking = true;
                void this.encoder.reconfigure({
                    bitrate: this.savedBitrate,
                    width: this.config.encoderConfig.width,
                    height: this.config.encoderConfig.height,
                });
                void this.encoder.forceKeyFrame();
            }
        }
    }

    /**
     * Update the base bitrate used for VAD-based reduction.
     * Call when server-driven quality changes arrive.
     */
    updateSavedBitrate(bitrate: number): void {
        this.savedBitrate = bitrate;
        // If currently silent, reapply the reduced ratio to the new base
        if (!this.isSpeaking) {
            const ratio = this.config.adaptiveFramerate?.reducedBitrateRatio ?? 0.25;
            const reducedBitrate = Math.round(this.savedBitrate * ratio);
            debugLog?.log(`updateSavedBitrate: silent, applying reduced bitrate ${reducedBitrate}`);
            void this.encoder.reconfigure({
                bitrate: reducedBitrate,
                width: this.config.encoderConfig.width,
                height: this.config.encoderConfig.height,
            });
        }
    }

    private setVadActive(isActive: boolean): void {
        if (isActive) {
            // Cancel pending silence timer
            if (this.vadSilenceTimer !== null) {
                clearTimeout(this.vadSilenceTimer);
                this.vadSilenceTimer = null;
            }

            // Restore from silence
            if (!this.isSpeaking) {
                this.isSpeaking = true;
                debugLog?.log('VAD: speech resumed, restoring full framerate and bitrate');

                // Restore bitrate
                void this.encoder.reconfigure({
                    bitrate: this.savedBitrate,
                    width: this.config.encoderConfig.width,
                    height: this.config.encoderConfig.height,
                });

                // Force keyframe for clean decode after gap
                void this.encoder.forceKeyFrame();
            }
        } else {
            // Start silence timer (debounce before reducing) — only in group calls (3+ streams)
            if (this.isSpeaking && this.vadSilenceTimer === null && this.remoteStreamCount >= 2) {
                const delay = this.config.adaptiveFramerate?.silenceDelayMs ?? 60_000;
                this.vadSilenceTimer = setTimeout(() => {
                    this.vadSilenceTimer = null;
                    this.isSpeaking = false;
                    this.lastPassedFrameTime = 0; // allow next frame through immediately

                    // Reduce bitrate
                    const ratio = this.config.adaptiveFramerate?.reducedBitrateRatio ?? 0.25;
                    const reducedBitrate = Math.round(this.savedBitrate * ratio);
                    debugLog?.log(`VAD: silence detected, reducing to ${this.config.adaptiveFramerate?.reducedFps ?? 5}fps, bitrate ${reducedBitrate}`);
                    void this.encoder.reconfigure({
                        bitrate: reducedBitrate,
                        width: this.config.encoderConfig.width,
                        height: this.config.encoderConfig.height,
                    });
                }, delay);
            }
        }
    }

    /**
   * Create canvas-based frame extractor (fallback for browsers without MSTP)
   * Enhanced for Safari compatibility with proper video element handling
   */
    private createCanvasFrameExtractor(videoTrack: MediaStreamTrack): ReadableStreamDefaultReader<VideoFrame> {
        infoLog?.log('Creating canvas-based frame extractor (Safari fallback)');

        const canvas = document.createElement('canvas');
        const video = document.createElement('video');

        // Safari-specific: Set attributes for autoplay and muted to allow playback
        video.autoplay = true;
        video.muted = true;
        video.playsInline = true;
        video.srcObject = new MediaStream([videoTrack]);

        const framerate = this.config.encoderConfig.framerate;
        const interval = 1000 / framerate;

        // Track frames that have been enqueued but not yet consumed
        const pendingFrames: VideoFrame[] = [];
        let pumpInterval: number | null = null;
        let videoReady = false;
        let metadataLoaded = false;
        let playPromise: Promise<void> | null = null;

        const stream = new ReadableStream<VideoFrame>({
            start: (controller) => {
                const pump = () => {
                    if (!this.processing) {
                        controller.close();
                        return;
                    }

                    // Check if video is actually ready to capture frames
                    if (!videoReady || video.paused || video.ended) {
                        // Video not ready yet, retry soon
                        pumpInterval = window.setTimeout(pump, 100);
                        return;
                    }

                    // Update canvas size if needed
                    if (canvas.width !== video.videoWidth || canvas.height !== video.videoHeight) {
                        canvas.width = video.videoWidth;
                        canvas.height = video.videoHeight;
                    }

                    const ctx = canvas.getContext('2d', { willReadFrequently: true });
                    if (ctx && video.videoWidth > 0 && video.videoHeight > 0) {
                        try {
                            ctx.drawImage(video, 0, 0);
                            const frame = new VideoFrame(canvas, {
                                timestamp: performance.now() * 1000 // microseconds
                            });

                            // Track the frame for cleanup
                            pendingFrames.push(frame);
                            controller.enqueue(frame);
                        } catch (error) {
                            errorLog?.log('Canvas frame extraction error:', error);
                        }
                    }

                    pumpInterval = window.setTimeout(pump, interval);
                };

                // Handle video events properly for Safari
                video.onloadedmetadata = () => {
                    infoLog?.log('Canvas extractor: video metadata loaded', video.videoWidth, 'x', video.videoHeight);
                    metadataLoaded = true;

                    // Try to play the video (Safari requires explicit play())
                    if (video.paused) {
                        playPromise = video.play().catch((error: unknown) => {
                            warnLog?.log('Canvas extractor: Video play() failed, trying to extract anyway:', error);
                            videoReady = true;
                            pump();
                        });

                        void playPromise.then(() => {
                            infoLog?.log('Canvas extractor: Video playback started');
                            videoReady = true;
                            pump();
                        }).catch(() => {
                            // Already handled above
                        });
                    } else {
                        videoReady = true;
                        pump(); // Start pumping frames
                    }
                };

                video.onerror = (e) => errorLog?.log('Canvas extractor video error:', e, video.error);

                // Fallback: if metadata never loads, start after timeout
                const fallbackTimeout = window.setTimeout(() => {
                    if (!metadataLoaded) {
                        warnLog?.log('Video metadata not loaded after timeout, attempting to extract frames anyway');
                        videoReady = true;
                        pump();
                    }
                }, 2000);

                // Cleanup timeout on cancellation
                // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
                const originalError: (e?: unknown) => void = controller.error.bind(controller);
                controller.error = (reason?: unknown) => {
                    clearTimeout(fallbackTimeout);
                    originalError(reason);
                };
            },

            cancel: () => {
                // Clean up any pending frames when stream is cancelled
                infoLog?.log(`Canvas frame extractor cancelled, closing ${pendingFrames.length} pending frames`);
                for (const frame of pendingFrames) {
                    try {
                        frame.close();
                    } catch (e: unknown) {
                        warnLog?.log('Error closing pending frame during cancellation:', e);
                    }
                }
                pendingFrames.length = 0;

                // Clear the pump interval
                if (pumpInterval) {
                    clearTimeout(pumpInterval);
                    pumpInterval = null;
                }
            }
        });

        // Create reader with cleanup tracking
        const reader = stream.getReader();

        // Override the reader's cancel method to ensure cleanup
        // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
        const originalCancel: (reason?: unknown) => Promise<void> = reader.cancel.bind(reader);
        reader.cancel = async (reason?: unknown) => {
            // Clean up any pending frames
            infoLog?.log(`Reader cancelled, closing ${pendingFrames.length} pending frames`);
            for (const frame of pendingFrames) {
                try {
                    frame.close();
                } catch (e: unknown) {
                    warnLog?.log('Error closing pending frame during reader cancellation:', e);
                }
            }
            pendingFrames.length = 0;

            // Clear the pump interval
            if (pumpInterval) {
                clearTimeout(pumpInterval);
                pumpInterval = null;
            }

            return originalCancel(reason);
        };

        // Track when frames are consumed to remove them from pending list
        // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
        const originalRead: () => Promise<ReadableStreamReadResult<VideoFrame>> = reader.read.bind(reader);
        reader.read = async () => {
            const result = await originalRead();

            // If we got a frame, remove it from pending (it will be closed by consumer)
            if (!result.done) {
                const frameIndex = pendingFrames.indexOf(result.value);
                if (frameIndex !== -1) {
                    pendingFrames.splice(frameIndex, 1);
                }
            }

            return result;
        };

        return reader;
    }


    private async pumpFrames(): Promise<void> {
        infoLog?.log('Starting frame pump...');
        let frameCount = 0;
        let droppedFrames = 0;

        try {
            while (this.processing) {
                const { done, value: frame } = await this.frameReader!.read();

                if (done) {
                    infoLog?.log(`Frame stream ended after ${frameCount} frames`);
                    break;
                }

                frameCount++;

                // Check if frame should be randomly dropped
                if (this.config.frameDropping?.enabled) {
                    const dropProbability = this.config.frameDropping.dropProbability ?? 0.1; // Default 10% drop rate
                    const randomValue = Math.random();
                    if (randomValue < dropProbability) {
                        frame.close();
                        droppedFrames++;
                        continue; // Skip processing this frame
                    }
                }

                // VAD-based adaptive framerate: drop frames when not speaking (group calls only)
                if (!this.isSpeaking && this.config.adaptiveFramerate?.enabled && this.remoteStreamCount >= 2) {
                    const now = performance.now();
                    if (now - this.lastPassedFrameTime < this.reducedFrameIntervalMs) {
                        frame.close();
                        droppedFrames++;
                        continue;
                    }
                    this.lastPassedFrameTime = now;
                }

                // Route frame through segmentation worker if background blur is enabled
                try {
                    if (this.segmentationWorker && this.config.backgroundBlur?.enabled) {
                        try {
                            // Send frame to segmentation worker for processing
                            await this.segmentationWorker.processFrame(frame, rpcNoWait);
                        } catch (error) {
                            errorLog?.log(`Segmentation worker error on frame #${frameCount}:`, error);
                            // Fallback: send frame directly to encoder if segmentation fails
                            await this.encoder.encodeFrame(frame);
                        }
                    } else {
                        // Send frame directly to encoder via RPC (frame is auto-transferred)
                        await this.encoder.encodeFrame(frame, rpcNoWait);
                    }
                } catch {
                    // Close frame if RPC transfer failed (e.g., worker shutting down)
                    try { frame.close(); } catch { /* already transferred/closed */ }
                }
            }
        } catch (error) {
            if (this.processing) {
                errorLog?.log(`Error pumping frames after ${frameCount} frames:`, error);
            }
        }
        infoLog?.log(`Frame pump stopped. Total frames pumped: ${frameCount}, dropped: ${droppedFrames}`);
    }



    private hasMSTPInWindow(): boolean {
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-explicit-any
        return typeof (globalThis as any).MediaStreamTrackProcessor === 'function';
    }
}

/**
 * Video Pipeline (UNIFIED ARCHITECTURE)
 * Universal two-worker architecture for all browsers using RPC communication.
 *
 * Architecture:
 * - Encoder Worker (universal, RPC-based)
 * - Decoder Worker (universal, RPC-based)
 * - TransferSimulator (local encode→decode loopback in main thread)
 * - Canvas fallbacks for browsers without MSTP/MSTG support
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';

import type { EncoderWorker } from '../workers/encoder-worker-contract';
import type { DecoderWorker as DecoderWorker } from '../workers/decoder-worker-contract';
import type { SegmentationWorker, SegmentationConfig, SegmentationStats, SegmentationWorkerCallbacks } from '../workers/segmentation-worker-contract';
import type { EncoderConfig, EncoderStats, EncodedChunkData } from '../webcodecs-encoder';
import { TransferSimulator, type TransferConfig, type TransferStats } from '../utils/transfer-simulator';
import type { DecoderConfig, DecoderStats } from '../webcodecs-decoder';
import { MediaStreamRecorder } from '../utils/mp4-muxer';
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

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoPipeline');

export interface PipelineConfig {
  encoderConfig: EncoderConfig;
  transferConfig: TransferConfig;
  decoderConfig: DecoderConfig;
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
}

// Type declarations for Insertable Streams API
declare class MediaStreamTrackProcessor<T = VideoFrame> {
    constructor(options: { track: MediaStreamTrack });
    readable: ReadableStream<T>;
}

declare class MediaStreamTrackGenerator<T = VideoFrame> extends MediaStreamTrack {
    constructor(options: { kind: 'video' | 'audio' });
    writable: WritableStream<T>;
}

export interface IVideoPipeline {
  start(inputStream: MediaStream): Promise<MediaStream>;
  stop(): Promise<Blob>;
  reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void>;
  toggleBlur(enabled: boolean, segmentationConfig?: SegmentationConfig): Promise<void>;
  switchSegmentationBackend(backend: 'webgpu' | 'wasm'): Promise<void>;
  toggleAV1Decoder(useWasm: boolean): Promise<void>;
  setPreviewCallback(callback: ((frame: VideoFrame) => void) | null): void;
  getEncoderStats(): EncoderStats;
  getTransferStats(): TransferStats;
  getDecoderStats(): DecoderStats;
  getSegmentationStats(): SegmentationStats | null;
}

export class VideoPipeline implements IVideoPipeline {
    private readonly encoderWorkerInstance: Worker;
    private readonly encoder: (EncoderWorker & Disposable);
    private readonly decoderWorkerInstance: Worker;
    private readonly decoder: (DecoderWorker & Disposable);
    private segmentationWorkerInstance: Worker | null = null;
    private segmentationWorker: (SegmentationWorker & Disposable) | null = null;
    private processor: MediaStreamTrackProcessor | null = null;
    private frameReader: ReadableStreamDefaultReader<VideoFrame> | null = null;
    private generator: MediaStreamTrackGenerator | null = null;
    private writer: WritableStreamDefaultWriter<VideoFrame> | null = null;

    // Network simulation (Chrome path only - represents boundary between sender and receiver)
    private transferSimulator: TransferSimulator | null = null;
    // Video streaming
    private videoStream: VideoStream | null = null; // VideoStream instance
    private pendingStreamFrames: VideoStreamFrame[] = []; // Buffer frames until we get codec description
    private codecSettings: string | null = null; // Base64 encoded codec description (SPS/PPS for H.264)
    private firstEncodedTimestamp: number | null = null; // First encoded chunk timestamp (microseconds) for 0-based normalization

    // Canvas fallbacks (when MSTG not available)
    private outputCanvas: HTMLCanvasElement | null = null;
    private outputCanvasCtx: CanvasRenderingContext2D | null = null;

    // Preview callback for rendering segmented frames before encoding
    private previewCallback: ((frame: VideoFrame) => void) | null = null;

    // Common
    private outputStream: MediaStream | null = null;
    private processing = false;
    private outputRecorder: MediaStreamRecorder | null = null;

    // Timestamp normalization for proper video playback
    private firstFrameTimestamp: number | null = null;

    private currentStats: {
    encoder: EncoderStats;
    transfer: TransferStats;
    decoder: DecoderStats;
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
            transfer: {
                totalBytes: 0,
                totalChunks: 0,
                averageChunkSize: 0,
                averageLatency: 0,
                packetsLost: 0,
                throughput: 0,
                jitter: 0,
                currentBitrate: 0
            },
            decoder: {
                decodedFrames: 0,
                droppedFrames: 0,
                averageDecodeTime: 0,
                hardwareAcceleration: 'unknown',
                resolution: 'N/A'
            },
            segmentation: null
        };
    private statsInterval: number | null = null;

    private onEncoderEncodedChunk = (chunkData: EncodedChunkData) => {
    // const chunkSeq = chunkData.sequenceNumber ?? -1;
    // console.log(`[Pipeline] Encoded chunk #${chunkSeq} received from sender via RPC: ${chunkData.type}, size: ${chunkData.byteLength}`);

        // Stream to server if enabled
        if (this.config.streaming?.enabled) {
            const chunkBytes = new Uint8Array(chunkData.byteLength);
            chunkData.chunk.copyTo(chunkBytes);
            // Normalize to 0-based offset so startedAtMs + offset gives correct epoch time
            this.firstEncodedTimestamp ??= chunkData.chunk.timestamp; // microseconds
            const normalizedTimestamp = chunkData.chunk.timestamp - this.firstEncodedTimestamp;
            // Convert microseconds to .NET TimeSpan ticks (100-nanosecond units)
            const frame: VideoStreamFrame = {
                offset: microsecondsToTicks(normalizedTimestamp),
                duration: microsecondsToTicks(chunkData.chunk.duration ?? 0),
                isKeyFrame: chunkData.type === 'key',
                width: this.config.encoderConfig.width,
                height: this.config.encoderConfig.height,
                data: chunkBytes
            };

            if (chunkData.type === 'key') {
                const offsetMs = normalizedTimestamp / 1000;
                debugLog?.log(`Streaming keyframe: seq=${chunkData.sequenceNumber}, offsetMs=${offsetMs.toFixed(0)}, ${(chunkBytes.length / 1024).toFixed(2)} KB`);
            }

            // Extract codec description from keyframes (required for H.264 decoder)
            if (chunkData.type === 'key' && chunkData.metadata?.decoderConfig?.description) {
                const desc = chunkData.metadata.decoderConfig.description;
                let descBytes: Uint8Array;
                if (desc instanceof ArrayBuffer) {
                    descBytes = new Uint8Array(desc);
                } else if (ArrayBuffer.isView(desc)) {
                    descBytes = new Uint8Array(desc.buffer, desc.byteOffset, desc.byteLength);
                } else {
                    descBytes = new Uint8Array(0);
                }
                frame.description = descBytes;

                // If we don't have codecSettings yet, capture it from this keyframe
                if (!this.codecSettings && descBytes.length > 0) {
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
                if (this.codecSettings) {
                    // We have the codec description, create the stream now
                    infoLog?.log(`Creating VideoStream with codecSettings (${this.codecSettings.length} chars)`);
                    const streamConfig: VideoStreamConfig = {
                        codec: this.config.encoderConfig.codec,
                        width: this.config.encoderConfig.width,
                        height: this.config.encoderConfig.height,
                        codecSettings: this.codecSettings,
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
                    // Buffer the frame until we get the codec description
                    this.pendingStreamFrames.push(frame);
                }
            } else {
                // Stream exists, send the frame directly
                this.videoStream.addFrame(frame);
            }
        }

        if (this.transferSimulator) {
            void this.transferSimulator.sendChunk(chunkData);
            // console.log(`[Pipeline] Chunk #${chunkSeq} (${chunkData.type}) delivered through network simulation to decoder`);
        }
    };

    private onDecoderDecodedFrame = async (frame: VideoFrame) => {
    // Normalize timestamp to be relative to first frame (fixes video playback position display)
        this.firstFrameTimestamp ??= frame.timestamp;
        // console.log(`[Pipeline] First frame timestamp set to: ${this.firstFrameTimestamp}μs`);
        const normalizedTimestamp = frame.timestamp - this.firstFrameTimestamp;
        // const originalSeconds = frame.timestamp / 1_000_000;
        // const normalizedSeconds = normalizedTimestamp / 1_000_000;

        // console.log(`[Pipeline] Received decoded frame: ${frame.displayWidth}x${frame.displayHeight}, original timestamp: ${frame.timestamp}μs (${originalSeconds.toFixed(2)}s), normalized: ${normalizedTimestamp}μs (${normalizedSeconds.toFixed(2)}s)`);

        // Create new frame with normalized timestamp for proper playback
        const normalizedFrame = new VideoFrame(frame, { timestamp: normalizedTimestamp });

        try {
            if (this.writer) {
                await this.writer.write(normalizedFrame);
                // console.log(`[Pipeline] Frame written to generator: ${frame.displayWidth}x${frame.displayHeight}`);
            } else if (this.outputCanvasCtx) {
                const frameWidth = frame.displayWidth;
                const frameHeight = frame.displayHeight;

                if (this.outputCanvas && (this.outputCanvas.width !== frameWidth || this.outputCanvas.height !== frameHeight)) {
                    // console.log(`[Pipeline] Adjusting output canvas from ${this.outputCanvas.width}x${this.outputCanvas.height} to ${frameWidth}x${frameHeight}`);
                    this.outputCanvas.width = frameWidth;
                    this.outputCanvas.height = frameHeight;
                }

                this.outputCanvasCtx.drawImage(frame, 0, 0, frameWidth, frameHeight);
                // console.log(`[Pipeline] Frame rendered to canvas: ${frameWidth}x${frameHeight}`);
            } else {
                errorLog?.log('No output method available, dropping frame');
            }
        } catch (error) {
            errorLog?.log('Error outputting decoded frame:', error);
        } finally {
            // Close both frames
            normalizedFrame.close();
            frame.close();
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
    // Create worker instances
        const encoderWorkerPath = Versioning.mapPath('/dist/videoEncoderWorker.js');
        infoLog?.log('Creating encoder worker from:', encoderWorkerPath);
        this.encoderWorkerInstance = new Worker(
            encoderWorkerPath,
            { type: 'module' }
        );
        this.encoderWorkerInstance.onerror = (e) => errorLog?.log('Encoder worker error:', e);

        const decoderWorkerPath = Versioning.mapPath('/dist/videoDecoderWorker.js');
        infoLog?.log('Creating decoder worker from:', decoderWorkerPath);
        this.decoderWorkerInstance = new Worker(
            decoderWorkerPath,
            { type: 'module' }
        );
        this.decoderWorkerInstance.onerror = (e) => errorLog?.log('Decoder worker error:', e);

        // Create RPC proxies
        this.encoder = rpcClientServer<EncoderWorker>(
            'VideoPipeline.encoder',
            this.encoderWorkerInstance,
            { onEncodedChunk: this.onEncoderEncodedChunk }
        );

        this.decoder = rpcClientServer<DecoderWorker>(
            'VideoPipeline.decoder',
            this.decoderWorkerInstance,
            { onDecodedFrame: this.onDecoderDecodedFrame }
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

    public async start(inputStream: MediaStream): Promise<MediaStream> {
        infoLog?.log('Starting unified video pipeline with RPC...');

        // Use unified two-worker architecture for all browsers
        infoLog?.log('Using unified architecture: two RPC workers with canvas fallbacks when needed');

        // Create output generator or canvas fallback
        if (this.hasMSTGInWindow()) {
            this.generator = new MediaStreamTrackGenerator({ kind: 'video' });
            this.writer = this.generator.writable.getWriter();
            infoLog?.log('Using MediaStreamTrackGenerator for output');
        } else if (this.hasVTGInWindow()) {
            // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-call, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-explicit-any
            const vtg: any = new (globalThis as any).VideoTrackGenerator();
            this.generator = vtg as MediaStreamTrackGenerator;
            this.writer = (vtg as MediaStreamTrackGenerator).writable.getWriter();
            infoLog?.log('Using VideoTrackGenerator for output');
        } else {
            // Canvas fallback for browsers without MSTG (older Safari)
            infoLog?.log('MSTG not available - using canvas-based output fallback');
            this.outputCanvas = document.createElement('canvas');
            this.outputCanvas.width = this.config.encoderConfig.width;
            this.outputCanvas.height = this.config.encoderConfig.height;
            this.outputCanvasCtx = this.outputCanvas.getContext('2d', { willReadFrequently: true });
            this.outputStream = this.outputCanvas.captureStream(this.config.encoderConfig.framerate);
        }

        // Setup transfer simulator for local encode→decode loopback
        this.transferSimulator = new TransferSimulator(
            this.config.transferConfig,
            (chunkData: EncodedChunkData) => {
                void (async () => {
                    await this.decoder.decodeChunk(chunkData);
                })();
            }
        );
        infoLog?.log('Transfer simulator initialized in main thread (network boundary)');

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

        // Initialize RPC proxies and wait for workers to be ready
        const initPromises = [
            this.encoder.initialize(this.config.encoderConfig, { type: 'rpc-timeout', timeoutMs: 5000 }),
            this.decoder.initialize(this.config.decoderConfig, { type: 'rpc-timeout', timeoutMs: 5000 })
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
        infoLog?.log('Decoder worker ready via RPC');

        // Start pumping frames to encoder worker
        // Note: this.processing was already set to true earlier (before frame extractor creation)
        void this.pumpFrames();

        // Start stats polling via RPC
        this.statsInterval = window.setInterval(() => {
            void (async () => {
                const encoderStats = await this.encoder.getStats();
                this.currentStats.encoder = encoderStats;
                const decoderStats = await this.decoder.getStats();
                this.currentStats.decoder = decoderStats;
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

        infoLog?.log('Pipeline started successfully with RPC: Encoder Worker (sender) -> TransferSimulator (network) -> Decoder Worker (receiver)');

        if (this.generator) {
            this.outputStream = new MediaStream([this.generator]);

            // Start recording the output stream with MediaRecorder for proper muxing
            const mimeType = this.getMediaRecorderMimeType();
            this.outputRecorder = new MediaStreamRecorder(mimeType);
            this.outputRecorder.start(this.outputStream);
            infoLog?.log(`Started MediaRecorder for output stream with MIME: ${mimeType}`);

            return this.outputStream;
        }

        // Use output stream from canvas if we created one
        if (this.outputStream) {
            // Start recording
            const mimeType = this.getMediaRecorderMimeType();
            this.outputRecorder = new MediaStreamRecorder(mimeType);
            this.outputRecorder.start(this.outputStream);
            infoLog?.log(`Started MediaRecorder for canvas output stream with MIME: ${mimeType}`);

            return this.outputStream;
        }

        throw new Error('Failed to create output stream');
    }

    public async stop(): Promise<Blob> {
        infoLog?.log('Stopping unified pipeline...');

        // Stop stats polling
        if (this.statsInterval) {
            clearInterval(this.statsInterval);
            this.statsInterval = null;
        }

        // Stop MediaRecorder first to get properly muxed video
        let recordedBlob: Blob | null = null;
        if (this.outputRecorder) {
            try {
                recordedBlob = await this.outputRecorder.stop();
                infoLog?.log(`MediaRecorder stopped, blob size: ${(recordedBlob.size / 1024 / 1024).toFixed(2)} MB`);
            } catch (error) {
                warnLog?.log('MediaRecorder stop error:', error);
            }
            this.outputRecorder = null;
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

        // Stop decoder worker via RPC
        await this.decoder.stop();
        infoLog?.log('Decoder stopped via RPC');

        // Stop segmentation worker if it exists
        if (this.segmentationWorker) {
            try {
                await this.segmentationWorker.stop();
                infoLog?.log('Segmentation worker stopped via RPC');
            } catch (error) {
                warnLog?.log('Error stopping segmentation worker:', error);
            }
        }

        // Close writer
        if (this.writer) {
            try {
                await this.writer.close();
                infoLog?.log('Writer closed');
            } catch (error) {
                warnLog?.log('Writer close error:', error);
            }
            this.writer = null;
        }

        // Stop generator
        if (this.generator) {
            this.generator.stop();
            infoLog?.log('Generator stopped');
            this.generator = null;
        }

        // Cleanup transfer mechanism
        if (this.transferSimulator) {
            this.transferSimulator.reset();
            this.transferSimulator = null;
        }

        // Complete video stream if active
        if (this.videoStream) {
            this.videoStream.complete();
            infoLog?.log('Video stream completed');
            this.videoStream = null;
        }

        // Reset timestamp normalization
        this.firstFrameTimestamp = null;
        this.firstEncodedTimestamp = null;

        // Cleanup RPC clients and worker instances
        this.encoder.dispose();
        this.decoder.dispose();
        this.encoderWorkerInstance.terminate();
        this.decoderWorkerInstance.terminate();

        if (this.segmentationWorker) {
            this.segmentationWorker.dispose();
            this.segmentationWorker = null;
        }
        if (this.segmentationWorkerInstance) {
            this.segmentationWorkerInstance.terminate();
            this.segmentationWorkerInstance = null;
        }

        if (this.outputStream) {
            this.outputStream.getTracks().forEach(track => track.stop());
            this.outputStream = null;
        }

        infoLog?.log('Pipeline stopped with RPC cleanup');

        // Return MediaRecorder blob if available, otherwise create empty blob
        return recordedBlob ?? new Blob([], { type: 'video/webm' });
    }


    // Safari-specific stop logic removed - now using unified architecture

    /**
   * Dynamically reconfigure encoder with new bitrate and/or resolution
   */
    async reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void> {
        infoLog?.log(`Reconfiguring via RPC: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);

        // Update config
        this.config.encoderConfig.bitrate = params.bitrate;
        this.config.encoderConfig.width = params.width;
        this.config.encoderConfig.height = params.height;

        // Unified: always reconfigure via RPC
        await this.encoder.reconfigure(params);
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

    getTransferStats(): TransferStats {
        if (this.transferSimulator) {
            return this.transferSimulator.getStats();
        }
        return { ...this.currentStats.transfer };
    }

    getDecoderStats(): DecoderStats {
        return { ...this.currentStats.decoder };
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
   * Toggle between WASM and built-in AV1 decoders
   */
    async toggleAV1Decoder(useWasm: boolean): Promise<void> {
        infoLog?.log(`Toggling AV1 decoder to ${useWasm ? 'WASM' : 'built-in'}`);

        try {
            // Toggle the decoder type via RPC
            await this.decoder.toggleDecoderType(useWasm);
            infoLog?.log(`Successfully toggled AV1 decoder to ${useWasm ? 'WASM' : 'built-in'}`);
        } catch (error) {
            errorLog?.log('Failed to toggle AV1 decoder:', error);
            throw error;
        }
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
                        await this.encoder.encodeFrame(frame);
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



    /**
    * Get MediaRecorder MIME type - uses WebM/VP9 for best compatibility
    */
    private getMediaRecorderMimeType(): string {
    // Try VP9 first (best quality and compatibility)
        if (MediaRecorder.isTypeSupported('video/webm;codecs=vp9')) {
            return 'video/webm;codecs=vp9';
        }
        // Try VP8 as fallback
        if (MediaRecorder.isTypeSupported('video/webm;codecs=vp8')) {
            return 'video/webm;codecs=vp8';
        }
        // Try H.264 in MP4
        if (MediaRecorder.isTypeSupported('video/mp4')) {
            return 'video/mp4';
        }
        // Let browser decide
        return '';
    }

    private hasMSTPInWindow(): boolean {
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-explicit-any
        return typeof (globalThis as any).MediaStreamTrackProcessor === 'function';
    }

    private hasMSTGInWindow(): boolean {
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-explicit-any
        return typeof (globalThis as any).MediaStreamTrackGenerator === 'function';
    }

    private hasVTGInWindow(): boolean {
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-explicit-any
        return typeof (globalThis as any).VideoTrackGenerator === 'function';
    }
}

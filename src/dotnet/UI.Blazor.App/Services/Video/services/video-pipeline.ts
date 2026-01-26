/**
 * Video Pipeline (UNIFIED ARCHITECTURE)
 * Universal two-worker architecture for all browsers using RPC communication.
 *
 * Architecture:
 * - Encoder Worker (universal, RPC-based)
 * - Decoder Worker (universal, RPC-based)
 * - TransferSimulator (network simulation in main thread)
 * - Canvas fallbacks for browsers without MSTP/MSTG support
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';

import type { EncoderWorker } from '../workers/encoder-worker-contract';
import type { DecoderWorker as DecoderWorker } from '../workers/decoder-worker-contract';
import type { SegmentationWorker, SegmentationConfig, SegmentationStats, SegmentationWorkerCallbacks } from '../workers/segmentation-worker-contract';
import type { EncoderConfig, EncoderStats, EncodedChunkData } from '../webcodecs-encoder';
import { TransferSimulator, type TransferConfig, type TransferStats } from '../utils/transfer-simulator';
import { WebSocketTransferAdapter } from '../utils/websocket-transfer';
import type { DecoderConfig, DecoderStats } from '../webcodecs-decoder';
import { MediaStreamRecorder } from '../utils/mp4-muxer';
import { VideoStreamer, type VideoStreamConfig, type VideoStreamFrame } from '../video-streamer';
import { Versioning } from 'versioning';

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
   * WebSocket transfer configuration (optional)
   * When enabled, uses real WebSocket communication instead of TransferSimulator
   */
  useWebSocketTransfer?: boolean;
  websocketServerUrl?: string;
  websocketRole?: 'sender' | 'receiver' | 'bidirectional';
  /**
   * Streaming configuration (optional)
   * When enabled, streams encoded chunks to server for real-time viewing
   */
  streaming?: {
    enabled: boolean;
    sessionToken: string;
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

declare class VideoTrackGenerator extends MediaStreamTrack {
  constructor();
  writable: WritableStream<VideoFrame>;
}

export interface IVideoPipeline {
  start(inputStream: MediaStream): Promise<MediaStream>;
  stop(): Promise<Blob>;
  reconfigure(params: { bitrate: number; width: number; height: number }): void;
  toggleBlur(enabled: boolean, segmentationConfig?: SegmentationConfig): Promise<void>;
  switchSegmentationBackend(backend: 'webgpu' | 'wasm'): Promise<void>;
  toggleAV1Decoder(useWasm: boolean): Promise<void>;
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
  private processor: MediaStreamTrackProcessor<VideoFrame> | null = null;
  private frameReader: ReadableStreamDefaultReader<VideoFrame> | null = null;
  private generator: MediaStreamTrackGenerator<VideoFrame> | null = null;
  private writer: WritableStreamDefaultWriter<VideoFrame> | null = null;

  // Network simulation (Chrome path only - represents boundary between sender and receiver)
  private transferSimulator: TransferSimulator | null = null;
  private websocketTransfer: WebSocketTransferAdapter | null = null;

  // Video streaming
  private videoStream: any = null; // VideoStream instance

  // Canvas fallbacks (when MSTG not available)
  private outputCanvas: HTMLCanvasElement | null = null;
  private outputCanvasCtx: CanvasRenderingContext2D | null = null;

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

  private onEncoderEncodedChunk = async (chunkData: EncodedChunkData) => {
    // const chunkSeq = chunkData.sequenceNumber ?? -1;
    // console.log(`[Pipeline] Encoded chunk #${chunkSeq} received from sender via RPC: ${chunkData.type}, size: ${chunkData.byteLength}`);

    // Stream to server if enabled
    if (this.config.streaming?.enabled && this.videoStream) {
      const chunkBytes = new Uint8Array(chunkData.byteLength);
      chunkData.chunk.copyTo(chunkBytes);
      const frame: VideoStreamFrame = {
        offset: chunkData.chunk.timestamp ?? chunkData.timestamp,
        duration: chunkData.chunk.duration ?? 0,
        isKeyFrame: chunkData.type === 'key',
        width: this.config.encoderConfig.width,
        height: this.config.encoderConfig.height,
        data: chunkBytes
      };
      this.videoStream.addFrame(frame);
      // console.log(`[Pipeline] Chunk (${chunkData.type}) streamed to server: ${chunkBytes.length} bytes`);
    }

    if (this.websocketTransfer) {
      await this.websocketTransfer.sendChunk(chunkData);
      console.log(`[Pipeline] Chunk (${chunkData.type}) sent via WebSocket to receiver`);
    } else if (this.transferSimulator) {
      await this.transferSimulator.sendChunk(chunkData);
      // console.log(`[Pipeline] Chunk #${chunkSeq} (${chunkData.type}) delivered through network simulation to decoder`);
    }
  };

  private onDecoderDecodedFrame = async (frame: VideoFrame) => {
    // Normalize timestamp to be relative to first frame (fixes video playback position display)
    if (this.firstFrameTimestamp === null) {
      this.firstFrameTimestamp = frame.timestamp;
      // console.log(`[Pipeline] First frame timestamp set to: ${this.firstFrameTimestamp}μs`);
    }
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
        console.error('[Pipeline] No output method available, dropping frame');
      }
    } catch (error) {
      console.error('[Pipeline] Error outputting decoded frame:', error);
    } finally {
      // Close both frames
      normalizedFrame.close();
      frame.close();
    }
  };

  private onSegmentationFrameProcessed = async (frame: VideoFrame, _sequenceNumber: number, _processingTime: number) => {
    console.log(`[Pipeline] Segmentation processed frame #${_sequenceNumber} in ${_processingTime.toFixed(2)}ms`);

    // Send processed frame to encoder
    if (this.encoder) {
      await this.encoder.encodeFrame(frame);
    } else {
      console.error('[Pipeline] No encoder available for processed frame');
      frame.close();
    }
  };

  private onSegmentationError = (error: Error) => {
    console.error(`[Pipeline] Segmentation error: ${error.message}`);
    // Could implement fallback logic here (e.g., disable blur and continue without it)
  };

  constructor(private config: PipelineConfig) {
    // Create worker instances
    const encoderWorkerPath = Versioning.mapPath('/dist/videoEncoderWorker.js');
    this.encoderWorkerInstance = new Worker(
      encoderWorkerPath,
      { type: 'module' }
    );

    const decoderWorkerPath = Versioning.mapPath('/dist/videoDecoderWorker.js');
    this.decoderWorkerInstance = new Worker(
      decoderWorkerPath,
      { type: 'module' }
    );

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
      console.log('[VideoPipeline] Creating segmentation worker for background blur with config:', this.config.backgroundBlur);
      const segmentationWorkerPath = Versioning.mapPath('/dist/videoSegmentationWorker.js');
      this.segmentationWorkerInstance = new Worker(
        segmentationWorkerPath,
        { type: 'module' }
      );

      this.segmentationWorker = rpcClientServer<SegmentationWorker>(
        'VideoPipeline.segmentation',
        this.segmentationWorkerInstance,
        {
          onFrameProcessed: this.onSegmentationFrameProcessed.bind(this),
          onError: this.onSegmentationError.bind(this)
        } as SegmentationWorkerCallbacks
      );
      console.log('[VideoPipeline] Segmentation worker created and RPC proxy initialized');
    } else {
      console.log('[VideoPipeline] Background blur not enabled in config:', this.config.backgroundBlur);
    }
  }

  public async start(inputStream: MediaStream): Promise<MediaStream> {
    console.log('Starting unified video pipeline with RPC...');

    // Use unified two-worker architecture for all browsers
    console.log('Using unified architecture: two RPC workers with canvas fallbacks when needed');

    // Create output generator or canvas fallback
    if (this.hasMSTGInWindow()) {
      this.generator = new MediaStreamTrackGenerator({ kind: 'video' });
      this.writer = this.generator.writable.getWriter();
      console.log('Using MediaStreamTrackGenerator for output');
    } else if (this.hasVTGInWindow()) {
      const vtg = new (globalThis as any).VideoTrackGenerator();
      this.generator = vtg as any;
      this.writer = (vtg as any).writable.getWriter();
      console.log('Using VideoTrackGenerator for output');
    } else {
      // Canvas fallback for browsers without MSTG (older Safari)
      console.log('MSTG not available - using canvas-based output fallback');
      this.outputCanvas = document.createElement('canvas');
      this.outputCanvas.width = this.config.encoderConfig.width;
      this.outputCanvas.height = this.config.encoderConfig.height;
      this.outputCanvasCtx = this.outputCanvas.getContext('2d', { willReadFrequently: true });
      this.outputStream = this.outputCanvas.captureStream(this.config.encoderConfig.framerate || 30);
    }

    // Setup transfer mechanism - use WebSocket for multi-device sessions, simulator for single-device testing
    if (this.config.useWebSocketTransfer) {
      console.log('[Pipeline] Using WebSocket transfer for multi-device session');
      this.websocketTransfer = new WebSocketTransferAdapter(
        {
          serverUrl: this.config.websocketServerUrl || 'ws://localhost:8080',
          role: this.config.websocketRole || 'sender',
          bandwidth: this.config.transferConfig.bandwidth,
          latency: this.config.transferConfig.latency,
          jitter: this.config.transferConfig.jitter,
          packetLoss: this.config.transferConfig.packetLoss
        },
        async (chunkData: EncodedChunkData) => {
          // Deliver chunk to decoder worker via RPC (received from WebSocket)
          if (this.decoder) {
            await this.decoder.decodeChunk(chunkData);
            console.log(`[Pipeline] WebSocket delivered ${chunkData.type} chunk to decoder via RPC`);
          }
        }
      );
      this.websocketTransfer.connect();
    } else {
      // Use transfer simulator for single-device testing
      this.transferSimulator = new TransferSimulator(
        this.config.transferConfig,
        async (chunkData: EncodedChunkData) => {
          // Deliver chunk to decoder worker via RPC (simulates receiving from network)
          if (this.decoder) {
            await this.decoder.decodeChunk(chunkData);
            // console.log(`[Pipeline] Network simulation delivered ${chunkData.type} chunk to decoder via RPC`);
          }
        }
      );
      console.log('[Pipeline] Transfer simulator initialized in main thread (network boundary)');
    }

    // Initialize video streaming if enabled
    if (this.config.streaming?.enabled) {
      console.log('[Pipeline] Initializing video streaming to server');

      // Initialize VideoStreamer SignalR connection
      const hubUrl = new URL('/api/hub/streams', window.location.origin).toString();
      VideoStreamer.init(hubUrl);

      const streamConfig: VideoStreamConfig = {
        codec: this.config.encoderConfig.codec,
        width: this.config.encoderConfig.width,
        height: this.config.encoderConfig.height,
      };
      this.videoStream = VideoStreamer.addStream(
        this.config.streaming.sessionToken,
        this.config.streaming.chatId,
        streamConfig
      );
      console.log('[Pipeline] Video streaming initialized');
    }


    // Get input video track
    const videoTrack = inputStream.getVideoTracks()[0];
    if (!videoTrack) {
      throw new Error('No video track found in input stream');
    }


    // IMPORTANT: Set processing to true BEFORE creating frame extractor
    // This fixes a race condition in Safari where the canvas fallback's pump()
    // function closes the stream immediately if processing is false
    this.processing = true;

    // Create processor to extract frames (with canvas fallback for older Safari)
    const hasMSTP = this.hasMSTPInWindow();
    console.log(`[Pipeline] MSTP available: ${hasMSTP}`);

    if (hasMSTP) {
      try {
        this.processor = new MediaStreamTrackProcessor({ track: videoTrack });
        this.frameReader = this.processor.readable.getReader();
        console.log('[Pipeline] Using MediaStreamTrackProcessor for frame extraction');
      } catch (error) {
        console.error('[Pipeline] MSTP creation failed, falling back to canvas:', error);
        this.frameReader = this.createCanvasFrameExtractor(videoTrack);
      }
    } else {
      // Canvas-based fallback for older browsers
      console.log('[Pipeline] MSTP not available - using canvas-based frame extraction fallback');
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
        ).catch(error => {
          console.error('[Pipeline] Failed to initialize segmentation worker:', error);
          throw error;
        })
      );
      console.log('[Pipeline] Initializing segmentation worker for background blur');
    }

    if (!this.encoder || !this.decoder) {
      throw new Error('RPC proxies not initialized');
    }

    await Promise.all(initPromises);
    console.log('[Pipeline] Encoder worker ready via RPC');
    console.log('[Pipeline] Decoder worker ready via RPC');

    // Start pumping frames to encoder worker
    // Note: this.processing was already set to true earlier (before frame extractor creation)
    this.pumpFrames();

    // Start stats polling via RPC
    this.statsInterval = window.setInterval(async () => {
      if (this.encoder) {
        const stats = await this.encoder.getStats();
        this.currentStats.encoder = stats;
      }
      if (this.decoder) {
        const stats = await this.decoder.getStats();
        this.currentStats.decoder = stats;
      }
      if (this.segmentationWorker) {
        try {
          const stats = await this.segmentationWorker.getStats();
          this.currentStats.segmentation = stats;
        } catch (error) {
          console.warn('[Pipeline] Failed to get segmentation stats:', error);
        }
      }
    }, 1000);

    console.log('Pipeline started successfully with RPC: Encoder Worker (sender) → TransferSimulator (network) → Decoder Worker (receiver)');

    if (this.generator) {
      this.outputStream = new MediaStream([this.generator]);

      // Start recording the output stream with MediaRecorder for proper muxing
      const mimeType = this.getMediaRecorderMimeType();
      this.outputRecorder = new MediaStreamRecorder(mimeType);
      this.outputRecorder.start(this.outputStream);
      console.log(`Started MediaRecorder for output stream with MIME: ${mimeType}`);

      return this.outputStream;
    }

    // Use output stream from canvas if we created one
    if (this.outputStream) {
      // Start recording
      const mimeType = this.getMediaRecorderMimeType();
      this.outputRecorder = new MediaStreamRecorder(mimeType);
      this.outputRecorder.start(this.outputStream);
      console.log(`Started MediaRecorder for canvas output stream with MIME: ${mimeType}`);

      return this.outputStream;
    }

    throw new Error('Failed to create output stream');
  }

  public async stop(): Promise<Blob> {
    console.log('Stopping unified pipeline...');

    // Stop stats polling
    if (this.statsInterval) {
      clearInterval(this.statsInterval);
      this.statsInterval = null;
    }

    // Always use unified stop logic
    if (!this.encoder || !this.decoder) {
      throw new Error('No active workers');
    }

    // Stop MediaRecorder first to get properly muxed video
    let recordedBlob: Blob | null = null;
    if (this.outputRecorder) {
      try {
        recordedBlob = await this.outputRecorder.stop();
        console.log(`MediaRecorder stopped, blob size: ${(recordedBlob.size / 1024 / 1024).toFixed(2)} MB`);
      } catch (error) {
        console.warn('MediaRecorder stop error:', error);
      }
      this.outputRecorder = null;
    }

    // Stop pumping frames
    this.processing = false;

    // Cancel frame reader
    if (this.frameReader) {
      try {
        await this.frameReader.cancel();
      } catch (e) {
        console.warn('Frame reader cancel error:', e);
      }
      this.frameReader = null;
    }

    // Stop encoder worker via RPC
    await this.encoder.stop();
    console.log('[Pipeline] Encoder stopped via RPC');

    // Stop decoder worker via RPC
    await this.decoder.stop();
    console.log('[Pipeline] Decoder stopped via RPC');

    // Stop segmentation worker if it exists
    if (this.segmentationWorker) {
      try {
        await this.segmentationWorker.stop();
        console.log('[Pipeline] Segmentation worker stopped via RPC');
      } catch (error) {
        console.warn('[Pipeline] Error stopping segmentation worker:', error);
      }
    }

    // Close writer
    if (this.writer) {
      try {
        await this.writer.close();
        console.log('Writer closed');
      } catch (error) {
        console.warn('Writer close error:', error);
      }
      this.writer = null;
    }

    // Stop generator
    if (this.generator) {
      this.generator.stop();
      console.log('Generator stopped');
      this.generator = null;
    }

    // Cleanup transfer mechanism
    if (this.websocketTransfer) {
      this.websocketTransfer.disconnect();
      this.websocketTransfer = null;
    } else if (this.transferSimulator) {
      this.transferSimulator.reset();
      this.transferSimulator = null;
    }

    // Complete video stream if active
    if (this.videoStream) {
      this.videoStream.complete();
      console.log('[Pipeline] Video stream completed');
      this.videoStream = null;
    }

    // Reset timestamp normalization
    this.firstFrameTimestamp = null;

    // Cleanup RPC clients and worker instances
    this.encoder.dispose();
    this.decoder.dispose();
    this.encoderWorkerInstance?.terminate();
    this.decoderWorkerInstance?.terminate();

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

    console.log('Pipeline stopped with RPC cleanup');

    // Return MediaRecorder blob if available, otherwise create empty blob
    return recordedBlob || new Blob([], { type: 'video/webm' });
  }


  // Safari-specific stop logic removed - now using unified architecture

  /**
   * Dynamically reconfigure encoder with new bitrate and/or resolution
   */
  async reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void> {
    console.log(`[VideoPipeline] Reconfiguring via RPC: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);

    // Update config
    this.config.encoderConfig.bitrate = params.bitrate;
    this.config.encoderConfig.width = params.width;
    this.config.encoderConfig.height = params.height;

    // Unified: always reconfigure via RPC
    if (this.encoder) {
      await this.encoder.reconfigure(params);
    }
  }

  /**
   * Dynamically toggle background blur on/off during recording
   */
  async toggleBlur(enabled: boolean, segmentationConfig?: SegmentationConfig): Promise<void> {
    console.log(`[VideoPipeline] Toggling background blur: ${enabled ? 'ON' : 'OFF'}`);

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

      console.log('[VideoPipeline] Initializing segmentation worker for dynamic blur enable...');

      this.segmentationWorkerInstance = new Worker(
        new URL('../workers/segmentation.worker.ts', import.meta.url),
        { type: 'module' }
      );

      this.segmentationWorker = rpcClientServer<SegmentationWorker>(
        'VideoPipeline.segmentation',
        this.segmentationWorkerInstance,
        {
          onFrameProcessed: this.onSegmentationFrameProcessed.bind(this),
          onError: this.onSegmentationError.bind(this)
        }
      );

      // Initialize the worker
      await this.segmentationWorker.initialize(
        this.config.backgroundBlur.segmentationConfig,
        { timeoutMs: 10000 }
      );

      console.log('[VideoPipeline] Segmentation worker initialized for dynamic blur toggle');
    }

    if (this.config.backgroundBlur) {
      this.config.backgroundBlur.enabled = enabled;
    }

    // Update segmentation worker config to enable/disable blur
    if (this.segmentationWorker) {
      await this.segmentationWorker.updateConfig({ blurEnabled: enabled });
      console.log(`[VideoPipeline] Updated segmentation worker blurEnabled to ${enabled}`);
    }

    console.log(`[VideoPipeline] Background blur ${enabled ? 'enabled' : 'disabled'}`);
  }

  getEncoderStats(): EncoderStats {
    return { ...this.currentStats.encoder };
  }

  getTransferStats(): TransferStats {
    // Get transfer stats from the appropriate transfer mechanism
    if (this.websocketTransfer) {
      return this.websocketTransfer.getStats();
    } else if (this.transferSimulator) {
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
   * Update segmentation configuration dynamically during recording
   */
  async updateSegmentationConfig(config: Partial<SegmentationConfig>): Promise<void> {
    console.log(`[VideoPipeline] Updating segmentation config:`, config);

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
    console.log(`[VideoPipeline] Switching segmentation backend to: ${newBackend}`);

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
      console.warn('[VideoPipeline] Error stopping current segmentation worker:', error);
    }

    // Update config with new backend
    const currentConfig = this.config.backgroundBlur.segmentationConfig!;
    const updatedConfig: SegmentationConfig = {
      ...currentConfig,
      backend: newBackend,
    };

    // Recreate worker instance
    this.segmentationWorkerInstance = new Worker(
      new URL('../workers/segmentation.worker.ts', import.meta.url),
      { type: 'module' }
    );

    // Recreate RPC proxy
    this.segmentationWorker = rpcClientServer<SegmentationWorker>(
      'VideoPipeline.segmentation',
      this.segmentationWorkerInstance,
      {
        onFrameProcessed: this.onSegmentationFrameProcessed.bind(this),
        onError: this.onSegmentationError.bind(this)
      } as SegmentationWorkerCallbacks
    );

    // Reinitialize with updated config
    await this.segmentationWorker.initialize(updatedConfig, { timeoutMs: 10000 });

    // Update local config
    this.config.backgroundBlur.segmentationConfig = updatedConfig;

    console.log(`[VideoPipeline] Successfully switched segmentation backend to ${newBackend}`);
  }

  /**
   * Toggle between WASM and built-in AV1 decoders
   */
  async toggleAV1Decoder(useWasm: boolean): Promise<void> {
    console.log(`[Pipeline] Toggling AV1 decoder to ${useWasm ? 'WASM' : 'built-in'}`);

    if (!this.decoder) {
      throw new Error('Decoder not available');
    }

    try {
      // Toggle the decoder type via RPC
      await this.decoder.toggleDecoderType(useWasm);
      console.log(`[Pipeline] Successfully toggled AV1 decoder to ${useWasm ? 'WASM' : 'built-in'}`);
    } catch (error) {
      console.error('[Pipeline] Failed to toggle AV1 decoder:', error);
      throw error;
    }
  }

  /**
   * Update frame dropping configuration dynamically during recording
   */
  updateFrameDroppingConfig(enabled: boolean, dropProbability: number = 0.1): void {
    console.log(`[VideoPipeline] Updating frame dropping config: enabled=${enabled}, probability=${dropProbability}`);

    // Create or update local config
    this.config.frameDropping = {
      enabled,
      dropProbability
    };

    console.log(`[VideoPipeline] Frame dropping config now:`, this.config.frameDropping);
  }

  /**
   * Create canvas-based frame extractor (fallback for browsers without MSTP)
   * Enhanced for Safari compatibility with proper video element handling
   */
  private createCanvasFrameExtractor(videoTrack: MediaStreamTrack): ReadableStreamDefaultReader<VideoFrame> {
    console.log('[Pipeline] Creating canvas-based frame extractor (Safari fallback)');

    const canvas = document.createElement('canvas');
    const video = document.createElement('video');

    // Safari-specific: Set attributes for autoplay and muted to allow playback
    video.autoplay = true;
    video.muted = true;
    video.playsInline = true;
    video.srcObject = new MediaStream([videoTrack]);

    const framerate = this.config.encoderConfig.framerate || 30;
    const interval = 1000 / framerate;

    // Track frames that have been enqueued but not yet consumed
    const pendingFrames: VideoFrame[] = [];
    let pumpInterval: number | null = null;
    let videoReady = false;
    let metadataLoaded = false;
    let playPromise: Promise<void> | null = null;

    // Capture processing state reference for closure
    const pipelineRef = this;

    const stream = new ReadableStream<VideoFrame>({
      start: (controller) => {
        const pump = () => {
          if (!pipelineRef.processing) {
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
              console.error('[Pipeline] Canvas frame extraction error:', error);
            }
          }

          pumpInterval = window.setTimeout(pump, interval);
        };

        // Handle video events properly for Safari
        video.onloadedmetadata = () => {
          console.log('[Pipeline] Canvas extractor: video metadata loaded', video.videoWidth, 'x', video.videoHeight);
          metadataLoaded = true;

          // Try to play the video (Safari requires explicit play())
          if (video.paused) {
            playPromise = video.play().catch(error => {
              console.warn('[Pipeline] Canvas extractor: Video play() failed, trying to extract anyway:', error);
              // Even if play() fails, we might still be able to extract frames
              videoReady = true;
              pump(); // Start pumping frames
            });

            if (playPromise) {
              playPromise.then(() => {
                console.log('[Pipeline] Canvas extractor: Video playback started');
                videoReady = true;
                pump(); // Start pumping frames
              }).catch(() => {
                // Already handled above
              });
            }
          } else {
            videoReady = true;
            pump(); // Start pumping frames
          }
        };

        video.onerror = (e) => console.error('[Pipeline] Canvas extractor video error:', e, video.error);

        // Fallback: if metadata never loads, start after timeout
        const fallbackTimeout = window.setTimeout(() => {
          if (!metadataLoaded) {
            console.warn('[Pipeline] Video metadata not loaded after timeout, attempting to extract frames anyway');
            videoReady = true;
            pump();
          }
        }, 2000);

        // Cleanup timeout on cancellation
        const originalCancel = controller.error.bind(controller);
        controller.error = (reason?: any) => {
          clearTimeout(fallbackTimeout);
          return originalCancel(reason);
        };
      },

      cancel: () => {
        // Clean up any pending frames when stream is cancelled
        console.log(`[Pipeline] Canvas frame extractor cancelled, closing ${pendingFrames.length} pending frames`);
        for (const frame of pendingFrames) {
          try {
            frame.close();
          } catch (e) {
            console.warn('[Pipeline] Error closing pending frame during cancellation:', e);
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
    const originalCancel = reader.cancel.bind(reader);
    reader.cancel = async (reason?: any) => {
      // Clean up any pending frames
      console.log(`[Pipeline] Reader cancelled, closing ${pendingFrames.length} pending frames`);
      for (const frame of pendingFrames) {
        try {
          frame.close();
        } catch (e) {
          console.warn('[Pipeline] Error closing pending frame during reader cancellation:', e);
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
    const originalRead = reader.read.bind(reader);
    reader.read = async () => {
      const result = await originalRead();

      // If we got a frame, remove it from pending (it will be closed by consumer)
      if (!result.done && result.value) {
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
    if (!this.frameReader || !this.encoder) {
      console.error('[Pipeline] Cannot pump frames: frameReader or encoder is null');
      return;
    }

    console.log('[Pipeline] Starting frame pump...');
    let frameCount = 0;
    let droppedFrames = 0;
    let consecutiveNullFrames = 0;
    const MAX_CONSECUTIVE_NULL_FRAMES = 10; // Safety limit

    try {
      while (this.processing) {
        const { done, value: frame } = await this.frameReader.read();

        if (done) {
          console.log(`[Pipeline] Frame stream ended after ${frameCount} frames`);
          break;
        }

        // Handle null frames (Safari canvas fallback issue)
        if (!frame) {
          consecutiveNullFrames++;
          console.warn(`[Pipeline] Null frame received #${consecutiveNullFrames} in a row`);

          if (consecutiveNullFrames >= MAX_CONSECUTIVE_NULL_FRAMES) {
            console.error(`[Pipeline] Too many consecutive null frames (${consecutiveNullFrames}), stopping pipeline`);
            this.processing = false;
            break;
          }

          // Small delay before retrying
          await new Promise(resolve => setTimeout(resolve, 50));
          continue;
        }

        consecutiveNullFrames = 0; // Reset counter
        frameCount++;

        if (frameCount % 30 === 1) { // Log every 30th frame
          console.log(`[Pipeline] Pumping frame #${frameCount}: ${frame.displayWidth}x${frame.displayHeight}, timestamp: ${frame.timestamp}μs`);
        }

        // Check if frame should be randomly dropped
        if (this.config.frameDropping?.enabled) {
          const dropProbability = this.config.frameDropping.dropProbability ?? 0.1; // Default 10% drop rate
          const randomValue = Math.random();
          if (randomValue < dropProbability) {
            console.log(`[Pipeline] Randomly dropping frame #${frameCount} (random=${randomValue.toFixed(3)} < probability=${dropProbability})`);
            frame.close();
            droppedFrames++;
            continue; // Skip processing this frame
          }
        } else if (frameCount % 300 === 0) {
          // Log every 300 frames to show frame dropping is disabled
          console.log(`[Pipeline] Frame dropping status at frame #${frameCount}:`, this.config.frameDropping);
        }

        // Route frame through segmentation worker if background blur is enabled
        if (this.segmentationWorker && this.config.backgroundBlur?.enabled) {
          try {
            // Send frame to segmentation worker for processing
            await this.segmentationWorker.processFrame(frame, rpcNoWait);
            console.log('[Pipeline] processFrame called on segmentation worker via RPC');
          } catch (error) {
            console.error(`[Pipeline] Segmentation worker error on frame #${frameCount}:`, error);
            // Fallback: send frame directly to encoder if segmentation fails
            await this.encoder.encodeFrame(frame);
          }
        } else {
          // Send frame directly to encoder via RPC (frame is auto-transferred)
          await this.encoder.encodeFrame(frame);
        }
      }
    } catch (error) {
      if (this.processing) {
        console.error(`[Pipeline] Error pumping frames after ${frameCount} frames:`, error);
      }
    }
    console.log(`[Pipeline] Frame pump stopped. Total frames pumped: ${frameCount}, dropped: ${droppedFrames}`);
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
    return typeof (globalThis as any).MediaStreamTrackProcessor === 'function';
  }

  private hasMSTGInWindow(): boolean {
    return typeof (globalThis as any).MediaStreamTrackGenerator === 'function';
  }

  private hasVTGInWindow(): boolean {
    return typeof (globalThis as any).VideoTrackGenerator === 'function';
  }
}

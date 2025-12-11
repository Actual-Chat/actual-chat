/**
 * WebCodecs Video Encoder
 * Encodes video frames to H.264 chunks with statistics tracking
 */

export interface EncoderConfig {
  codec: string; // Support any codec string to handle H.264, HEVC, AV1, VP9, etc.
  width: number;
  height: number;
  bitrate: number;
  framerate: number;
  keyframeInterval: number;
  latencyMode: 'realtime' | 'quality';
  hardwareAcceleration: 'prefer-hardware' | 'prefer-software';
  scalabilityMode?: string; // Scalability mode like 'L1T1', 'L1T2', 'L1T3'
}

export interface EncodedChunkData {
  chunk: EncodedVideoChunk;
  metadata: EncodedVideoChunkMetadata | undefined;
  timestamp: number;
  type: 'key' | 'delta';
  byteLength: number;
  sequenceNumber: number; // Added for chunk ordering to prevent out-of-order delivery issues
}

export interface EncoderStats {
  encodedFrames: number;
  droppedFrames: number;
  keyFrames: number;
  totalBytes: number;
  averageEncodeTime: number;
  hardwareAcceleration: string;
}

export class WebCodecsEncoder {
  private encoder: VideoEncoder;
  private frameCount = 0;
  private droppedFrames = 0;
  private keyFrameCount = 0;
  private lastKeyFrame = 0;
  private totalBytes = 0;
  private encodeTimeHistory: number[] = [];
  private encodeStartTimes: number[] = [];
  private chunkSequence = 0; // Track chunk sequence for proper ordering

  constructor(
    private config: EncoderConfig,
    private onChunk: (chunk: EncodedChunkData) => void,
    private onError: (error: Error) => void
  ) {
    this.encoder = new VideoEncoder({
      output: (chunk, metadata) => {
        // Track encode time - pop the start time from queue
        const startTime = this.encodeStartTimes.shift();
        if (startTime !== undefined) {
          const encodeTime = performance.now() - startTime;
          this.encodeTimeHistory.push(encodeTime);
          if (this.encodeTimeHistory.length > 100) {
            this.encodeTimeHistory.shift();
          }
        }

        const chunkData: EncodedChunkData = {
          chunk,
          metadata,
          timestamp: chunk.timestamp,
          type: chunk.type,
          byteLength: chunk.byteLength,
          sequenceNumber: this.chunkSequence++
        };

        this.totalBytes += chunk.byteLength;
        if (chunk.type === 'key') {
          this.keyFrameCount++;
        }

        this.onChunk(chunkData);
      },
      error: (e) => {
        console.error('WebCodecs Encoder error:', e);
        this.onError(e);
      }
    });
  }

  initialize(): void {
    try {
      console.log(`[Encoder] Initializing with: ${this.config.width}x${this.config.height} @ ${(this.config.bitrate / 1_000_000).toFixed(1)}Mbps`);
      
      const encoderConfig: VideoEncoderConfig = {
        codec: this.config.codec,
        width: this.config.width,
        height: this.config.height,
        bitrate: this.config.bitrate,
        framerate: this.config.framerate,
        latencyMode: this.config.latencyMode,
        hardwareAcceleration: this.config.hardwareAcceleration,
      };

      // Add scalability mode if specified
      if (this.config.scalabilityMode) {
        encoderConfig.scalabilityMode = this.config.scalabilityMode;
      }

      // Add codec-specific config based on codec type
      if (this.config.codec.startsWith('avc1')) {
        encoderConfig.avc = { format: 'avc' }; // AVCC format provides description in metadata
      } else if (this.config.codec.startsWith('hev1') || this.config.codec.startsWith('hvc1')) {
        // HEVC format - use type assertion since TypeScript types may not include HEVC yet
        (encoderConfig as any).hevc = { format: 'hevc' }; // HEVC format provides description in metadata
      } else if (this.config.codec.startsWith('av01')) {
        // AV1 doesn't need additional format configuration
        // The codec string itself contains all necessary information
      }

      if (this.config.scalabilityMode) {
        console.log(`Using scalability mode: ${this.config.scalabilityMode}`);
      }
      this.encoder.configure(encoderConfig);
    } catch (error) {
      console.error('Failed to configure encoder:', error);
      throw error;
    }
  }

  encode(frame: VideoFrame, forceKeyFrame = false): void {
    if (this.encoder.state !== 'configured') {
      this.droppedFrames++;
      frame.close();
      return;
    }

    // Record start time for async timing measurement
    this.encodeStartTimes.push(performance.now());

    // Determine if this should be a keyframe
    const shouldBeKeyFrame = forceKeyFrame ||
      (this.frameCount - this.lastKeyFrame >= this.config.keyframeInterval);

    if (shouldBeKeyFrame) {
      this.lastKeyFrame = this.frameCount;
      console.log('[Encoder] 🔑 KEYFRAME at frame', this.frameCount,
        '(forced:', forceKeyFrame,
        'interval reached:', (this.frameCount - this.lastKeyFrame >= this.config.keyframeInterval),
        'input frame:', `${frame.displayWidth}x${frame.displayHeight}`,
        'encoder config:', `${this.config.width}x${this.config.height} @ ${this.config.bitrate / 1_000_000}Mbps`);
    }

    try {
      this.encoder.encode(frame, { keyFrame: shouldBeKeyFrame });
      this.frameCount++;
    } catch (error) {
      this.droppedFrames++;
      // Remove the start time since encode failed
      this.encodeStartTimes.pop();
      console.error('Error encoding frame:', error);
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
        console.error('Error flushing encoder:', error);
      }
    }
  }

  async reconfigure(params: { bitrate?: number; width?: number; height?: number }): Promise<void> {
    if (this.encoder.state !== 'configured') {
      throw new Error('Encoder is not configured');
    }

    console.log('[Encoder] 🔧 RECONFIGURE called with:', params);
    console.log('[Encoder] Current state - frameCount:', this.frameCount, 'lastKeyFrame:', this.lastKeyFrame);

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

    console.log('[Encoder] Config changed from:',
      `${oldBitrate / 1_000_000}Mbps ${oldWidth}x${oldHeight}`,
      'to:',
      `${this.config.bitrate / 1_000_000}Mbps ${this.config.width}x${this.config.height}`
    );

    const encoderConfig: VideoEncoderConfig = {
      codec: this.config.codec,
      width: this.config.width,
      height: this.config.height,
      bitrate: this.config.bitrate,
      framerate: this.config.framerate,
      latencyMode: this.config.latencyMode,
      hardwareAcceleration: this.config.hardwareAcceleration
    };

    // Add scalability mode if specified
    if (this.config.scalabilityMode) {
      encoderConfig.scalabilityMode = this.config.scalabilityMode;
    }

    // Add codec-specific config based on codec type
    if (this.config.codec.startsWith('avc1')) {
      encoderConfig.avc = { format: 'avc' };
    } else if (this.config.codec.startsWith('hev1') || this.config.codec.startsWith('hvc1')) {
      // HEVC format - use type assertion since TypeScript types may not include HEVC yet
      (encoderConfig as any).hevc = { format: 'hevc' };
    } else if (this.config.codec.startsWith('av01')) {
      // AV1 doesn't need additional format configuration
    }

    // Reconfigure the encoder
    console.log('[Encoder] Calling VideoEncoder.configure() with new bitrate:', (encoderConfig.bitrate || 0) / 1_000_000, 'Mbps');
    this.encoder.configure(encoderConfig);
    console.log('[Encoder] VideoEncoder.configure() completed');
    
    // FIX: Force immediate keyframe on next frame by setting lastKeyFrame far enough in the past
    // This ensures the condition (frameCount - lastKeyFrame >= keyframeInterval) will be true on next encode
    this.lastKeyFrame = this.frameCount - this.config.keyframeInterval;
    console.log('[Encoder] ✅ Keyframe counter adjusted to force IMMEDIATE keyframe on next frame');
    console.log('[Encoder] 🔑 Next frame (frame', this.frameCount, ') will be encoded as keyframe with new bitrate:', this.config.bitrate / 1_000_000, 'Mbps');
  }

  close(): void {
    if (this.encoder.state !== 'closed') {
      this.encoder.close();
    }
  }

  getState(): CodecState {
    return this.encoder.state;
  }

  getStats(): EncoderStats {
    const averageEncodeTime = this.encodeTimeHistory.length > 0
      ? this.encodeTimeHistory.reduce((a, b) => a + b, 0) / this.encodeTimeHistory.length
      : 0;

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
    } catch (e) {
      hardwareAcceleration = 'unknown';
    }

    return {
      encodedFrames: this.frameCount,
      droppedFrames: this.droppedFrames,
      keyFrames: this.keyFrameCount,
      totalBytes: this.totalBytes,
      averageEncodeTime,
      hardwareAcceleration
    };
  }

  reset(): void {
    this.frameCount = 0;
    this.droppedFrames = 0;
    this.keyFrameCount = 0;
    this.lastKeyFrame = 0;
    this.totalBytes = 0;
    this.encodeTimeHistory = [];
    this.encodeStartTimes = [];
    this.chunkSequence = 0;
  }
}
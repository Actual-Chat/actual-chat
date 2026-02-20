/**
 * Recording Service
 * Manages recording lifecycle, stream acquisition, and pipeline coordination
 */

import { VideoPipeline, type PipelineConfig } from './video-pipeline';
import { getBestScalabilityMode } from '../codec-support';
import { detectGPUBackends } from '../gpu-support';
import type { SegmentationConfig } from '../workers/segmentation-worker-contract';
import { createDefaultSegmentationConfig, createAdaptiveSegmentationConfig } from '../workers/segmentation-worker-contract';
import { Log } from 'logging';

const { infoLog, warnLog, errorLog } = Log.get('VideoPipeline');

export interface RecordingConfig {
  mode: 'webcam' | 'screen';
  codec: 'h264' | 'av1';
  codecString?: string; // Specific codec profile string (e.g., 'avc1.640028', 'av01.0.08M.08')
  scalabilityModes?: string[]; // Supported scalability modes for the selected codec
  width: number;
  height: number;
  bitrate: number;
  framerate: number;
  cameraDeviceId?: string; // Specific camera device ID for webcam mode
  // Transfer simulation settings
  bandwidth: number;
  latency: number;
  jitter: number;
  packetLoss: number;
  // Streaming settings
  streaming?: {
    enabled: boolean;
    chatId: string;
  };
  // Background blur settings
  backgroundBlur?: {
    enabled: boolean;
    segmentationConfig?: SegmentationConfig;
  };
  // Frame dropping settings
  frameDropping?: {
    enabled: boolean;
    dropProbability?: number;
  };
  // AV1 decoder settings
  av1Decoder?: {
    enabled: boolean;
    backend: 'wasm' | 'builtin';
  };
}

export interface RecordingState {
  isRecording: boolean;
  duration: number;
  status: string;
}

export class RecordingService extends EventTarget {
    private config: RecordingConfig;
    private pipeline: VideoPipeline /*| AV1VideoPipeline*/ | null = null;
    private inputStream: MediaStream | null = null;
    private outputStream: MediaStream | null = null;
    private state: RecordingState = {
        isRecording: false,
        duration: 0,
        status: 'ready'
    };
    private startTime = 0;
    private durationInterval: number | null = null;

    constructor(initialConfig: RecordingConfig) {
        super();
        this.config = initialConfig;
    }

    updateConfig(config: Partial<RecordingConfig>): void {
        this.config = { ...this.config, ...config };
    }

    async toggleBlur(enabled: boolean): Promise<void> {
        infoLog?.log('Toggling blur', enabled ? 'ON' : 'OFF');

        if (!this.pipeline) {
            throw new Error('No active pipeline');
        }

        // Update config
        let segmentationConfig: SegmentationConfig | undefined;
        if (enabled && !this.config.backgroundBlur) {
            // Initialize background blur config if enabling and it doesn't exist
            const gpuSupport = await detectGPUBackends();

            segmentationConfig = createAdaptiveSegmentationConfig(gpuSupport.recommended);

            this.config.backgroundBlur = {
                enabled: true,
                segmentationConfig
            };
        } else if (this.config.backgroundBlur) {
            this.config.backgroundBlur.enabled = enabled;
            segmentationConfig = this.config.backgroundBlur.segmentationConfig;
        }

        // Toggle in pipeline
        await this.pipeline.toggleBlur(enabled, segmentationConfig);
    }

    updateSegmentationBackend(backend: 'webgpu' | 'webgl' | 'wasm'): void {
        infoLog?.log('Updating segmentation backend to', backend);

        if (this.config.backgroundBlur) {
            let segConfig = this.config.backgroundBlur.segmentationConfig;
            if (!segConfig) {
                // Create segmentation config if it doesn't exist
                segConfig = createAdaptiveSegmentationConfig(backend);
                this.config.backgroundBlur.segmentationConfig = segConfig;
            } else {
                // Update existing config
                segConfig.backend = backend;
            }
        } else {
            // Create background blur config if it doesn't exist
            const segConfig = createAdaptiveSegmentationConfig(backend);
            this.config.backgroundBlur = {
                enabled: false,
                segmentationConfig: segConfig
            };
        }
    }

    updateSegmentationBlurRadius(blurRadius: number): void {
        infoLog?.log('Updating blur radius to', blurRadius);

        if (this.config.backgroundBlur) {
            if (this.config.backgroundBlur.segmentationConfig) {
                this.config.backgroundBlur.segmentationConfig.blurRadius = blurRadius;
            } else {
                // Create segmentation config if it doesn't exist
                this.config.backgroundBlur.segmentationConfig = {
                    ...createDefaultSegmentationConfig('webgpu'),
                    blurRadius
                };
            }
        } else {
            // Create background blur config if it doesn't exist
            this.config.backgroundBlur = {
                enabled: false,
                segmentationConfig: {
                    ...createAdaptiveSegmentationConfig('webgpu'),
                    blurRadius
                }
            };
        }
    }

    async toggleAV1Decoder(useWasm: boolean): Promise<void> {
        infoLog?.log('Toggling AV1 decoder to', useWasm ? 'WASM' : 'built-in');

        if (!this.pipeline) {
            throw new Error('No active pipeline');
        }

        // Toggle in pipeline
        await this.pipeline.toggleAV1Decoder(useWasm);
    }

    updateFrameDropping(enabled: boolean, dropProbability = 0.1): void {
        infoLog?.log(`Updating frame dropping: ${enabled ? 'enabled' : 'disabled'} ${dropProbability * 100}%`);

        this.config.frameDropping = {
            enabled,
            dropProbability
        };
    }

    async start(): Promise<void> {
        try {
            this.setState({ status: 'acquiring-media' });
            infoLog?.log('Acquiring media stream...');

            // Get input stream based on mode
            this.inputStream = await this.acquireMediaStream();

            // Get actual video dimensions from the stream
            const videoTrack = this.inputStream.getVideoTracks()[0];
            const settings = videoTrack.getSettings();
            const actualWidth = settings.width ?? this.config.width;
            const actualHeight = settings.height ?? this.config.height;

            infoLog?.log(`Actual video dimensions: ${actualWidth}x${actualHeight}`);

            this.setState({ status: 'initializing-pipeline' });
            infoLog?.log('Initializing pipeline with', this.config.codec.toUpperCase(), 'codec');

            // Create and start pipeline with actual dimensions
            /*if (this.config.codec === 'av1') {
        // const av1PipelineConfig = await this.buildAV1PipelineConfig(actualWidth, actualHeight);
        // this.pipeline = new AV1VideoPipeline(av1PipelineConfig);
      } else {*/
            const pipelineConfig = await this.buildPipelineConfig(actualWidth, actualHeight);
            this.pipeline = new VideoPipeline(pipelineConfig);
            // }
            this.outputStream = await this.pipeline.start(this.inputStream);

            // Start duration tracking
            this.startTime = performance.now();
            this.durationInterval = window.setInterval(() => {
                this.updateDuration();
            }, 100);

            this.setState({
                isRecording: true,
                status: 'recording'
            });

            infoLog?.log('Recording started');
        } catch (error) {
            errorLog?.log('Start failed:', error);

            // Provide user-friendly error message for codec issues
            let errorMessage = 'Failed to start recording';
            if (error instanceof Error) {
                if (error.message.includes('not supported')) {
                    errorMessage = `${this.config.codec.toUpperCase()} codec is not supported in your browser. Please try using H.264 instead.`;
                } else {
                    errorMessage = error.message;
                }
            }

            this.setState({ status: `error: ${errorMessage}` });

            const enhancedError = new Error(errorMessage);
            this.dispatchEvent(new CustomEvent('error', { detail: enhancedError }));
            throw enhancedError;
        }
    }

    async stop(): Promise<void> {
        if (!this.pipeline) {
            return;
        }

        infoLog?.log('Stopping recording...');
        this.setState({ status: 'stopping' });

        // Stop duration tracking
        if (this.durationInterval) {
            clearInterval(this.durationInterval);
            this.durationInterval = null;
        }

        // Stop input stream
        if (this.inputStream) {
            this.inputStream.getTracks().forEach(track => track.stop());
            this.inputStream = null;
        }

        // Stop pipeline to tear down streaming and unregister backend stream
        try {
            await this.pipeline.stop();
        } catch (error) {
            warnLog?.log('Pipeline stop error:', error);
        }
        this.pipeline = null;

        this.setState({
            isRecording: false,
            status: 'idle'
        });

        infoLog?.log('Recording stopped');
    }

    private async acquireMediaStream(): Promise<MediaStream> {
        if (this.config.mode === 'webcam') {
            const videoConstraints: MediaTrackConstraints = {
                width: { ideal: this.config.width },
                height: { ideal: this.config.height },
                frameRate: { ideal: this.config.framerate }
            };

            if (this.config.cameraDeviceId) {
                videoConstraints.deviceId = { exact: this.config.cameraDeviceId };
            }

            return navigator.mediaDevices.getUserMedia({
                video: videoConstraints,
                audio: false
            });
        } else {
            return navigator.mediaDevices.getDisplayMedia({
                video: {
                    width: { ideal: this.config.width },
                    height: { ideal: this.config.height }
                },
                audio: false
            });
        }
    }

    private async buildPipelineConfig(width: number, height: number): Promise<PipelineConfig> {
    // Use the specific codec string if provided, otherwise use defaults
        let codecString: string;

        if (this.config.codecString) {
            codecString = this.config.codecString;
        } else {
            // Fallback to defaults based on codec category
            if (this.config.codec === 'av1') {
                codecString = 'av01.0.08M.08'; // AV1 Main, Level 4.0
            } else {
                codecString = 'avc1.640028'; // H.264 High profile level 4.0 (supports up to 1920x1088 @ 30fps, 2073600 coded area)
            }
        }

        // Determine best scalability mode if available
        const scalabilityMode = this.config.scalabilityModes
            ? getBestScalabilityMode(this.config.scalabilityModes)
            : undefined;

        infoLog?.log('Using codec string:', codecString, 'for', this.config.codec);
        if (scalabilityMode) {
            infoLog?.log('Using scalability mode:', scalabilityMode);
        }

        const pipelineConfig: PipelineConfig = {
            encoderConfig: {
                codec: codecString,
                width: width,
                height: height,
                bitrate: this.config.bitrate,
                framerate: this.config.framerate,
                keyframeInterval: 30, // 1 keyframe per second at 30fps
                latencyMode: 'realtime',
                hardwareAcceleration: 'prefer-hardware',
                scalabilityMode: scalabilityMode
            },
            transferConfig: {
                bandwidth: this.config.bandwidth,
                latency: this.config.latency,
                jitter: this.config.jitter,
                packetLoss: this.config.packetLoss
            },
            decoderConfig: {
                codec: codecString, // Match encoder codec
                optimizeForLatency: true,
                hardwareAcceleration: 'prefer-hardware'
            }
        };

        // Add background blur configuration if enabled
        if (this.config.backgroundBlur?.enabled) {
            // Use existing segmentation config if available, otherwise create with detected backend
            let segmentationConfig: SegmentationConfig;

            if (this.config.backgroundBlur.segmentationConfig) {
                // Use the configured backend
                segmentationConfig = { ...this.config.backgroundBlur.segmentationConfig };
                infoLog?.log('Background blur enabled with', segmentationConfig.backend, 'backend');
            } else {
                // Fallback: detect GPU capabilities to determine best backend
                const gpuSupport = await detectGPUBackends();
                segmentationConfig = createDefaultSegmentationConfig(gpuSupport.recommended);
                infoLog?.log('Background blur enabled with', gpuSupport.recommended, 'backend');
            }

            pipelineConfig.backgroundBlur = {
                enabled: true,
                segmentationConfig
            };
        }

        // Add frame dropping configuration if enabled
        if (this.config.frameDropping?.enabled) {
            pipelineConfig.frameDropping = {
                enabled: true,
                dropProbability: this.config.frameDropping.dropProbability ?? 0.1
            };
            infoLog?.log(`Frame dropping enabled with ${(this.config.frameDropping.dropProbability ?? 0.1) * 100}% drop probability`);
        }

        // Add streaming configuration if enabled
        if (this.config.streaming?.enabled) {
            pipelineConfig.streaming = {
                enabled: true,
                chatId: this.config.streaming.chatId,
            };
            infoLog?.log('Streaming enabled to chat', this.config.streaming.chatId);
        }

        return pipelineConfig;
    }

    private updateDuration(): void {
        if (this.startTime > 0) {
            const duration = (performance.now() - this.startTime) / 1000;
            this.setState({ duration });
        }
    }

    private setState(partial: Partial<RecordingState>): void {
        this.state = { ...this.state, ...partial };
        this.dispatchEvent(new CustomEvent('state-change', {
            detail: this.state
        }));
    }

    private cleanup(): void {
        if (this.inputStream) {
            this.inputStream.getTracks().forEach(track => track.stop());
            this.inputStream = null;
        }
        this.outputStream = null;
        this.pipeline = null;
        this.startTime = 0;
    }

    getState(): RecordingState {
        return { ...this.state };
    }

    getInputStream(): MediaStream | null {
        return this.inputStream;
    }

    getOutputStream(): MediaStream | null {
        return this.outputStream;
    }

    /**
   * Set a callback to receive processed (blurred) frames for local preview.
   * Must be called after start().
   */
    setPreviewCallback(callback: ((frame: VideoFrame) => void) | null): void {
        if (this.pipeline) {
            this.pipeline.setPreviewCallback(callback);
        }
    }

    getPipeline(): VideoPipeline /*| AV1VideoPipeline*/ | null {
        return this.pipeline;
    }
}

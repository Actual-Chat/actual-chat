/**
 * Recording Service
 * Manages recording lifecycle, stream acquisition, and pipeline coordination
 */

import { VideoPipeline, type PipelineConfig } from './video-pipeline';
import { getBestScalabilityMode, getCodecCategory, getCodecForCategory } from '../codec-support';
import { detectGPUBackends } from '../gpu-support';
import type { SegmentationConfig } from '../workers/video-processing-worker-contract';
import { createDefaultSegmentationConfig, createAdaptiveSegmentationConfig } from '../workers/video-processing-worker-contract';
import { MediaCapture } from './media-capture';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

export interface RecordingConfig {
  mode: 'webcam' | 'screen';
  codec: 'h264' | 'hevc' | 'av1' | 'vp9';
  codecString?: string; // Specific codec profile string (e.g., 'avc1.640028', 'av01.0.08M.08')
  hardwareAccelerated?: boolean; // Whether the selected codec is hardware accelerated
  scalabilityModes?: string[]; // Supported scalability modes for the selected codec
  width: number;
  height: number;
  bitrate: number;
  framerate: number;
  cameraDeviceId?: string; // Specific camera device ID for webcam mode
  cameraFacingMode?: 'user' | 'environment'; // Alternative to deviceId — used when flipping front/back on mobile
  preferHighRes?: boolean; // Hint the browser toward the main lens when picking by facingMode
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
  // VAD-based adaptive framerate settings
  adaptiveFramerate?: {
    enabled: boolean;
    reducedFps?: number;
    reducedBitrateRatio?: number;
    silenceDelayMs?: number;
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
    private inputTrack: MediaStreamTrack | null = null;
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

    updateFrameDropping(enabled: boolean, dropProbability = 0.1): void {
        infoLog?.log(`Updating frame dropping: ${enabled ? 'enabled' : 'disabled'} ${dropProbability * 100}%`);

        this.config.frameDropping = {
            enabled,
            dropProbability
        };
    }

    async switchCodec(codec: string): Promise<void> {
        if (!this.pipeline) {
            warnLog?.log('switchCodec: no active pipeline');
            return;
        }

        const codecString = getCodecForCategory(codec as 'h264' | 'hevc' | 'av1' | 'vp9', this.config.width, this.config.height);

        // Skip if already using the same codec category
        const currentCategory = getCodecCategory(this.config.codecString ?? '');
        const targetCategory = getCodecCategory(codecString);
        if (currentCategory === targetCategory)
            return;

        // No isConfigSupported check here — the sender already validated encoder
        // capabilities at 1080p during recording start (cached in video-recorder.ts).
        // Checking at current resolution can produce false positives (e.g. Android
        // reports AV1 support at 720p but silently falls back to H.264).

        infoLog?.log(`Switching codec from ${this.config.codec} to ${codec} (${codecString})`);
        this.config.codec = codec as 'h264' | 'hevc' | 'av1' | 'vp9';
        this.config.codecString = codecString;

        await this.pipeline.switchCodec(codecString);
    }

    async start(): Promise<void> {
        try {
            this.setState({ status: 'acquiring-media' });
            infoLog?.log('Acquiring media stream...');

            // Get input track based on mode
            this.inputTrack = await this.acquireMediaTrack();

            // Get actual video dimensions from the track
            const settings = this.inputTrack.getSettings();
            infoLog?.log(`acquireMediaStream: track settings:`, JSON.stringify(settings));
            let actualWidth = settings.width ?? this.config.width;
            let actualHeight = settings.height ?? this.config.height;

            // For screencast: cap initial resolution to 1080p to avoid sending 4K keyframes
            // before the quality preset arrives (~1s). Floor at 720p for text readability.
            if (this.config.mode === 'screen') {
                const maxInitialWidth = 1920;
                const maxInitialHeight = 1080;
                const minWidth = 1280;
                const minHeight = 720;
                if (actualWidth > maxInitialWidth || actualHeight > maxInitialHeight) {
                    const scale = Math.min(maxInitialWidth / actualWidth, maxInitialHeight / actualHeight);
                    actualWidth = Math.max(minWidth, Math.round(actualWidth * scale));
                    actualHeight = Math.max(minHeight, Math.round(actualHeight * scale));
                }
            }

            infoLog?.log(`Actual video dimensions: ${actualWidth}x${actualHeight}`);

            this.setState({ status: 'initializing-pipeline' });
            infoLog?.log('Initializing pipeline with', this.config.codec.toUpperCase(), 'codec');

            const pipelineConfig = await this.buildPipelineConfig(actualWidth, actualHeight);
            this.pipeline = new VideoPipeline(pipelineConfig);

            // Wire up encoder failure fallback — switch to H264 if current codec dies
            this.pipeline.onEncoderFailure = (failedCodec: string) => {
                const category = getCodecCategory(failedCodec);
                if (category === 'h264') {
                    errorLog?.log(`H264 encoder also failed — no fallback available`);
                    return;
                }
                warnLog?.log(`Encoder failed for ${category}, falling back to H264`);
                this.dispatchEvent(new CustomEvent('encoder-failure', { detail: category }));
                void this.switchCodec('h264');
            };

            await this.pipeline.start(new MediaStream([this.inputTrack]));

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

        // Stop input track
        if (this.inputTrack) {
            this.inputTrack.stop();
            this.inputTrack = null;
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

    private async acquireMediaTrack(): Promise<MediaStreamTrack> {
        if (this.config.mode === 'webcam') {
            return MediaCapture.captureCameraStream({
                deviceId: this.config.cameraDeviceId,
                facingMode: this.config.cameraFacingMode,
                preferHighRes: this.config.preferHighRes,
                frameRate: this.config.framerate,
                width: this.config.width,
                height: this.config.height,
            });
        } else {
            return MediaCapture.captureScreencast();
        }
    }

    private async buildPipelineConfig(width: number, height: number): Promise<PipelineConfig> {
    // Firefox: cap to 720p — higher resolutions cause encoder failures
        if (DeviceInfo.isFirefox && height > 720) {
            width = Math.round(width * (720 / height));
            height = 720;
        }

        // iOS: cap H.264 to 540p (AVCC overhead makes 720p too slow at ~160ms/frame)
        // HEVC at 720p is fine (HW accelerated)
        if (DeviceInfo.isIos && this.config.codec === 'h264' && height > 540) {
            width = Math.round(width * (540 / height));
            height = 540;
        }

        // Mobile webcam: cap to 720p — mobile cameras often ignore getUserMedia
        // resolution hints and return native sensor resolution (e.g. 1920x2560),
        // which exceeds H.264 level 3.1 coded area limits (921,600 pixels).
        if (this.config.mode === 'webcam' && DeviceInfo.isMobile) {
            const maxDim = 1280;
            if (width > maxDim || height > maxDim) {
                const scale = Math.min(maxDim / width, maxDim / height);
                width = Math.round(width * scale) & ~1;
                height = Math.round(height * scale) & ~1;
            }
        }

        // Use the specific codec string if provided, otherwise use defaults
        let codecString: string;

        if (this.config.codecString) {
            codecString = this.config.codecString;
        } else {
            codecString = getCodecForCategory(this.config.codec, width, height);
        }

        // Determine best scalability mode if available
        const scalabilityMode = this.config.scalabilityModes
            ? getBestScalabilityMode(this.config.scalabilityModes)
            : undefined;

        infoLog?.log('Using codec string:', codecString, 'for', this.config.codec);
        if (scalabilityMode) {
            infoLog?.log('Using scalability mode:', scalabilityMode);
            if (scalabilityMode === 'L1T1')
                infoLog?.log('SVC temporal layers (L1T2/L1T3) not supported on this hardware');
        }

        const pipelineConfig: PipelineConfig = {
            encoderConfig: {
                codec: codecString,
                width: width,
                height: height,
                bitrate: this.config.bitrate,
                framerate: this.config.framerate,
                // Webcam: 2-3s interval (less frequent keyframes save bandwidth).
                // Screencast: 1-2s interval (more frequent for text clarity on content switches).
                keyframeInterval: this.config.mode === 'screen'
                    ? this.config.framerate * 2   // ~2s for screencast
                    : this.config.framerate * 3,  // ~3s for webcam
                // Wall-clock floor — guarantees a keyframe even when frames arrive slowly
                // (VAD-reduced path, static screencast). Paired with server-side fast-join
                // (VideoStreamFilter) so late joiners always find a keyframe in retention.
                maxKeyFrameIntervalMs: this.config.mode === 'screen' ? 2000 : 3000,
                latencyMode: 'realtime',
                hardwareAcceleration: this.config.hardwareAccelerated ? 'prefer-hardware' : 'no-preference',
                scalabilityMode: scalabilityMode
            },
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
                streamKind: this.config.mode === 'screen' ? 1 : 0,
            };
            infoLog?.log('Streaming enabled to chat', this.config.streaming.chatId, 'streamKind:', this.config.mode);
        }

        // Add adaptive framerate configuration if enabled
        if (this.config.adaptiveFramerate?.enabled) {
            pipelineConfig.adaptiveFramerate = { ...this.config.adaptiveFramerate };
            infoLog?.log('Adaptive framerate enabled');
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
        if (this.inputTrack) {
            this.inputTrack.stop();
            this.inputTrack = null;
        }
        this.pipeline = null;
        this.startTime = 0;
    }

    getState(): RecordingState {
        return { ...this.state };
    }

    getInputTrack(): MediaStreamTrack | null {
        return this.inputTrack;
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

    getConfig(): RecordingConfig {
        return { ...this.config };
    }
}

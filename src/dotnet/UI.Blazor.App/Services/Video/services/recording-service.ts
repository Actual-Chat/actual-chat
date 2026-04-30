/**
 * Recording Service
 * Manages recording lifecycle, stream acquisition, and pipeline coordination
 */

import { VideoPipeline, type PipelineConfig } from './video-pipeline';
import { getCodecCategory, getCodecForCategory } from '../codec-support';
import { detectGPUBackends } from '../gpu-support';
import type { SegmentationConfig, SpatialLayerConfig } from '../workers/video-processing-worker-contract';
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
  // Full simulcast ladder, sorted bottom-first to match the spatial-id
  // convention used everywhere: `ladder[0]` is the base layer (primary
  // encoder runs at these dims, SpatialLayerId = 0); `ladder[last]` is the
  // top / capture ideal. Intermediate entries become extra encoders with
  // ascending spatial IDs (ladder[i] → SpatialLayerId = i). Omit for
  // single-encoder / P2P. The ladder is re-clamped after camera acquire —
  // tiers that exceed the camera's actual output get dropped so the
  // downscaler never upscales from a smaller source.
  simulcastLadder?: SpatialLayerConfig[];
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

    // Hot reconfig of simulcast layers on the live pipeline. `ladder` is the
    // FULL bottom-first ladder (base + extras); we split off base internally.
    // Mid-stream activation (Option C) — bypasses stop/start so the active
    // RPC stream stays registered and other peers don't see stream churn.
    async setSimulcastLadder(ladder: SpatialLayerConfig[] | null): Promise<void> {
        if (!this.pipeline) return;
        // Cache for next fresh start as well.
        this.config.simulcastLadder = ladder ?? undefined;

        // If the new ladder's base dims/bitrate differ from the live encoder,
        // reconfigure the base encoder first. Without this, mid-stream
        // simulcast activation leaves the base at its original (often top-tier)
        // dims while extras include the same dims — identical duplicate
        // spatial layers, downscaler runs N identity slots.
        if (ladder && ladder.length > 0) {
            const base = ladder[0];
            const stats = this.pipeline.getEncoderStats();
            if (stats.configuredWidth !== base.width
                || stats.configuredHeight !== base.height
                || stats.configuredBitrate !== base.bitrate) {
                infoLog?.log(
                    `setSimulcastLadder: reconfiguring base ${stats.configuredWidth}x${stats.configuredHeight}@${(stats.configuredBitrate / 1_000_000).toFixed(2)}Mbps`
                    + ` → ${base.width}x${base.height}@${(base.bitrate / 1_000_000).toFixed(2)}Mbps`);
                await this.pipeline.reconfigure({
                    width: base.width, height: base.height, bitrate: base.bitrate,
                });
            }
        }

        // Strip base — pipeline.setSpatialLayers takes EXTRAS only (base lives
        // on encoderConfig). Empty/single-tier ladder → empty extras → P2P.
        const extras = ladder && ladder.length > 1 ? ladder.slice(1) : [];
        await this.pipeline.setSpatialLayers(extras);
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

            // Video codecs (H.264, HEVC, etc.) require even dimensions. getDisplayMedia
            // can return odd sizes (e.g., window/tab capture at 1365x767). Round down
            // to even — the 1px crop is invisible, and the resize canvas handles the
            // mismatch when the first frame arrives at the original odd size.
            actualWidth &= ~1;
            actualHeight &= ~1;

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

            // Clamp the ladder to the camera's actual output. Any tier whose
            // dims exceed the real camera resolution would force the downscaler
            // to upscale — no new detail, wasted bandwidth. If every tier is
            // larger than the camera, fall back to a single tier at the
            // camera's actual dims (inheriting the base tier's bitrate so the
            // encoder isn't over-allocated).
            let ladder = this.config.simulcastLadder;
            let spatialLayers: SpatialLayerConfig[] | undefined;
            let encW = actualWidth;
            let encH = actualHeight;
            let encBitrate = this.config.bitrate;
            if (ladder && ladder.length > 0) {
                // Ladder is bottom-first: ladder[0] = base, ladder[last] = top.
                // Clamp by filtering out tiers the camera can't realize; order
                // preserved (filter is stable, camera can only cap the top end).
                const fits = ladder.filter(l =>
                    l.width <= actualWidth && l.height <= actualHeight);
                let clamped: SpatialLayerConfig[];
                if (fits.length === 0) {
                    // Camera below every ladder tier — substitute a single tier
                    // at actual dims, inheriting the base tier's bitrate budget.
                    const fallbackBitrate = ladder[0].bitrate;
                    clamped = [{ width: actualWidth, height: actualHeight, bitrate: fallbackBitrate }];
                    warnLog?.log(`Camera ${actualWidth}x${actualHeight} below every ladder tier — using single encoder at camera dims`);
                } else {
                    clamped = fits;
                    if (clamped.length !== ladder.length)
                        infoLog?.log(`Clamped ladder to camera: [${clamped.map(l => `${l.width}x${l.height}`).join(', ')}]`);
                }
                ladder = clamped;

                const base = ladder[0];
                encW = base.width;
                encH = base.height;
                encBitrate = base.bitrate;
                // Extras = everything above base, already in ascending order
                // (bottom-first), so `ladder.slice(1)[i]` → SpatialLayerId = i+1
                // in the worker. Single-tier ladder → no extras → P2P path.
                spatialLayers = ladder.length > 1 ? ladder.slice(1) : undefined;
            }

            const pipelineConfig = await this.buildPipelineConfig(encW, encH, encBitrate);
            if (spatialLayers)
                pipelineConfig.spatialLayers = spatialLayers;
            this.pipeline = new VideoPipeline(pipelineConfig);

            // Wire up encoder failure fallback. Recording-service doesn't know
            // the HW encoder / audience decoder intersection — that lives on
            // the caller (video-recorder). Dispatch the event; caller picks
            // the next codec from its priority chain and calls switchCodec.
            this.pipeline.onEncoderFailure = (failedCodec: string) => {
                const category = getCodecCategory(failedCodec);
                warnLog?.log(`Encoder failed for ${category} — emitting encoder-failure event`);
                this.dispatchEvent(new CustomEvent('encoder-failure', { detail: category }));
            };

            // Track-end detection. Camera was unexpectedly revoked —
            // dispatch a user-visible error and stop the pipeline so callers
            // can decide whether to retry. Without this, the worker logged
            // `Stream input ended` and the pipeline died silently.
            this.pipeline.onTrackEnded = () => {
                warnLog?.log('Camera track ended unexpectedly — stopping recording');
                this.setState({ status: 'error: Camera was disconnected (another app may have taken it)' });
                this.dispatchEvent(new CustomEvent('error', {
                    detail: new Error('Camera was disconnected (another app may have taken it)'),
                }));
                // Best-effort stop. Caller (video-recorder) listens to the
                // `error` event and decides on retry policy; we just clean up.
                void this.stop().catch((e: unknown) => errorLog?.log('Stop after track-end failed:', e));
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

            // Provide user-friendly error messages
            let errorMessage = 'Failed to start recording';
            if (error instanceof DOMException && error.name === 'NotReadableError') {
                // Camera device exists in the OS enumeration but can't deliver frames
                // (e.g. virtual cameras like Meta Quest 2 that are registered but idle).
                errorMessage = await this.describeUnavailableCamera();
            } else if (error instanceof Error) {
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

    private async describeUnavailableCamera(): Promise<string> {
        const deviceId = this.config.cameraDeviceId;
        if (!deviceId) return 'Camera is unavailable';
        try {
            const devices = await navigator.mediaDevices.enumerateDevices();
            const label = devices
                .find(d => d.kind === 'videoinput' && d.deviceId === deviceId)
                ?.label;
            return label ? `Camera '${label}' is unavailable` : 'Camera is unavailable';
        } catch {
            return 'Camera is unavailable';
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
                frameRate: this.config.framerate,
                width: this.config.width,
                height: this.config.height,
            });
        } else {
            return MediaCapture.captureScreencast();
        }
    }

    private async buildPipelineConfig(width: number, height: number, bitrate?: number): Promise<PipelineConfig> {
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
                width = Math.round(width * scale);
                height = Math.round(height * scale);
            }
        }

        // Ensure even dimensions — video codecs require it. Applied after all
        // platform-specific caps whose Math.round can produce odd values.
        width &= ~1;
        height &= ~1;

        // Use the specific codec string if provided, otherwise use defaults
        let codecString: string;

        if (this.config.codecString) {
            codecString = this.config.codecString;
        } else {
            codecString = getCodecForCategory(this.config.codec, width, height);
        }

        // Temporal SVC (L1T2/L1T3) disabled — Chrome HEVC HW + L1T2 raises async
        // OperationError on some HW. Single-layer configure is universally supported.
        const scalabilityMode: string | undefined = undefined;

        infoLog?.log('Using codec string:', codecString, 'for', this.config.codec);

        const pipelineConfig: PipelineConfig = {
            encoderConfig: {
                codec: codecString,
                width: width,
                height: height,
                bitrate: bitrate ?? this.config.bitrate,
                framerate: this.config.framerate,
                // Webcam: 2-3s interval (less frequent keyframes save bandwidth).
                // Screencast: 1-2s interval (more frequent for text clarity on content switches).
                keyframeInterval: this.config.mode === 'screen'
                    ? this.config.framerate * 2   // ~2s for screencast (active scrolling)
                    : this.config.framerate * 3,  // ~3s for webcam
                // Wall-clock floor — guarantees a keyframe even when frames arrive slowly.
                // Screencast heartbeat feeds 1 frame/s on static content; at 2s cap every 2nd
                // heartbeat got promoted to keyframe (~600kbps pure heartbeat). Raised to 10s
                // so static-screen heartbeats stay mostly P-frames (tiny). New joiners are
                // served by the PLI path: server requests keyframe on peer join → next
                // heartbeat is promoted via forceKeyFrame. Active scrolling (15 fps) hits
                // the count-based cap (framerate*2 = 30 frames) at 2s anyway.
                maxKeyFrameIntervalMs: this.config.mode === 'screen' ? 10000 : 3000,
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

        // spatialLayers is assigned by `start()` after the camera-driven clamp.
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

    /** WYSIWYG preview track produced inside the worker (post-rotate, post-downscale).
     *  Null on browsers without MSTG support — caller falls back to {@link getInputTrack}. */
    getProcessedTrack(): MediaStreamTrack | null {
        return this.pipeline?.getProcessedTrack() ?? null;
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

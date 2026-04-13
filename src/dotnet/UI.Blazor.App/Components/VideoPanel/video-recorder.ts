import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { RecordingService, type RecordingConfig, type RecordingState } from '../../Services/Video/services/recording-service';
import { detectSupportedCodecs, getDefaultCodec, getCodecCategory, type CodecInfo } from '../../Services/Video/codec-support';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoRecorder');

export interface OwnStreamDiagnostics {
    mode: string;
    codec: string;
    codecCategory: string;
    hardwareAccelerated: boolean;
    inputResolution: string;
    inputFramerate: number;
    outputResolution: string;
    configuredBitrate: number;
    actualBitrateKbps: number;
    encodedFrames: number;
    droppedFrames: number;
    keyFrames: number;
    medianEncodeTime: number;
    pureMedianEncodeTime: number;
    encoderHwAccel: string;
    duration: number;
    cameraLabel: string | null;
    blurEnabled: boolean;
    segmentationBackend: string | null;
    segmentationAvgTime: number | null;
    supportedEncoderCategories: string[];
    status: string;
}

export interface VideoDevice {
    deviceId: string;
    label: string;
}

// Module-level singleton so the modal and preview can find the active recorder
let activeRecorderInstance: VideoRecorder | null = null;
export function getActiveRecorder(): VideoRecorder | null {
    return activeRecorderInstance;
}

interface Size {
    width: number;
    height: number;
}

export class VideoRecorder {
    private blazorRef: DotNet.DotNetObject;
    // Video recording service (using video-pipeline)
    private recordingService: RecordingService | null = null;
    private isRecording = false;
    private isScreencasting = false;
    private previewTrack: MediaStreamTrack | null = null;
    private selectedCameraDeviceId: string | null = null;
    private chatId = '';
    private isBlurEnabled = false;
    private blurToggleChain: Promise<void> = Promise.resolve();
    private disposed = false;
    private lastStatus = '';
    private cameraWidth = 0;
    private cameraHeight = 0;
    private previewPaused = false;
    // Cached encoder capabilities (detected at recording start)
    private supportedEncoderCategories: string[] = [];
    private supportedCodecs: CodecInfo[] = [];

    /** External callback for blur preview frames — set by VideoStreamingPreview */
    public onPreviewFrame: ((frame: VideoFrame) => void) | null = null;

    static create(blazorRef: DotNet.DotNetObject): VideoRecorder {
        return new VideoRecorder(blazorRef);
    }

    static async enumerateDevices(): Promise<VideoDevice[]> {
        try {
            // Request permission first to get device labels
            const tempStream = await navigator.mediaDevices.getUserMedia({ video: true });
            tempStream.getTracks().forEach(t => t.stop());

            const devices = await navigator.mediaDevices.enumerateDevices();
            const videoDevices = devices
                .filter(d => d.kind === 'videoinput')
                .map(d => ({
                    deviceId: d.deviceId,
                    label: d.label || `Camera ${d.deviceId.slice(0, 8)}`,
                }));
            infoLog?.log('Enumerated video devices:', videoDevices);
            return videoDevices;
        } catch (error) {
            errorLog?.log('Failed to enumerate video devices:', error);
            return [];
        }
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
    }

    /**
     * Set the selected camera device
     */
    public setSelectedCamera(deviceId: string): void {
        this.selectedCameraDeviceId = deviceId;
        infoLog?.log('Selected camera device:', deviceId);
    }

    /**
     * Switch camera during active recording by stopping and restarting with the new device.
     */
    public async switchCamera(deviceId: string): Promise<void> {
        this.selectedCameraDeviceId = deviceId;
        infoLog?.log('Switching camera to:', deviceId);

        if (!this.isRecording || !this.recordingService) {
            infoLog?.log('Not recording — camera will be used on next start');
            return;
        }

        try {
            // Tear down current recording silently (no Blazor notification)
            this.cleanupPreviewTrack();
            await this.recordingService.stop();
            this.recordingService = null;
            this.isRecording = false;

            // Restart with the new camera
            await this.startRecording(this.chatId);
        } catch (error) {
            errorLog?.log('Failed to switch camera:', error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', String(error));
        }
    }

    /**
     * Set whether background blur should be enabled when recording starts
     */
    public setBlurEnabled(enabled: boolean): void {
        this.isBlurEnabled = enabled;
        infoLog?.log('Background blur enabled:', enabled);
    }

    /**
     * Forward remote stream count to the video pipeline for slowdown decisions
     */
    public setRemoteStreamCount(count: number): void {
        this.recordingService?.getPipeline()?.setRemoteStreamCount(count);
    }

    /**
     * Toggle blur on an active recording
     */
    public toggleBlur(enabled: boolean): void {
        this.isBlurEnabled = enabled;
        if (this.recordingService) {
            const rs = this.recordingService;
            this.blurToggleChain = this.blurToggleChain
                .then(() => rs.toggleBlur(enabled))
                .catch((e: unknown) => warnLog?.log('Failed to toggle blur:', e));
        }
    }

    /**
     * Get the preview stream (wraps previewTrack) for sharing with the settings modal.
     */
    public getPreviewStream(): MediaStream | null {
        if (this.previewTrack?.readyState !== 'live') return null;
        return new MediaStream([this.previewTrack]);
    }

    /**
     * Get the raw preview track for rendering by VideoStreamingPreview.
     */
    public getPreviewTrack(): MediaStreamTrack | null {
        return this.previewTrack;
    }

    /**
     * Get the device ID of the currently selected camera.
     */
    public getPreviewDeviceId(): string | null {
        return this.selectedCameraDeviceId;
    }

    /**
     * Whether blur is currently active on this recorder.
     */
    public isBlurActive(): boolean {
        return this.isBlurEnabled;
    }

    /**
     * Whether this recorder is currently in screencast mode.
     */
    public isScreencastActive(): boolean {
        return this.isScreencasting;
    }

    /**
     * Whether preview rendering is paused (modal is open).
     */
    public isPreviewPaused(): boolean {
        return this.previewPaused;
    }

    /**
     * Pause the preview rendering (so only the modal draws while it's open).
     */
    public pausePreviewRendering(): void {
        this.previewPaused = true;
    }

    /**
     * Resume the preview rendering.
     */
    public resumePreviewRendering(): void {
        this.previewPaused = false;
    }

    /**
     * Initialize and start video recording
     */
    public async startRecording(chatId: string, audienceCodecs?: string[]): Promise<void> {
        this.chatId = chatId;
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        infoLog?.log('Starting video recording...');

        try {
            // Capture at 720p on all platforms — lower resolutions may select the wrong
            // camera on Android and produce aspect-ratio mismatches.
            const targetSize = { width: 1280, height: 720 };
            const targetBitrate = 4_000_000; // Must match VideoQualityPreset.High to avoid immediate reconfigure
            const targetFramerate = 30;

            // Detect supported encoder codecs — use target resolution to avoid
            // false negatives on mobile (e.g. Android HEVC encoders may not support 1080p)
            const supportedCodecs = await detectSupportedCodecs(targetSize.width, targetSize.height);
            this.supportedCodecs = supportedCodecs;

            // Cache supported encoder categories for later codec negotiation
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);
            infoLog?.log(`Supported encoder categories: [${this.supportedEncoderCategories.join(', ')}]`);

            // Pick initial codec: if audience codecs are known, pick the best encoder
            // codec that the audience can decode — avoids mid-stream codec switches
            const bestCodecString = this.pickInitialCodec(supportedCodecs, audienceCodecs, targetSize);
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);
            const codecCategory = getCodecCategory(bestCodecString);
            infoLog?.log(`Initial codec selection: ${codecCategory} (${bestCodecString}), hw=${bestCodecInfo?.hardwareAccelerated ?? false}`);

            // Create recording service with streaming config (uses video-pipeline internally)
            const config: RecordingConfig = {
                mode: 'webcam',
                codec: codecCategory,
                codecString: bestCodecString,
                hardwareAccelerated: bestCodecInfo?.hardwareAccelerated ?? false,
                scalabilityModes: bestCodecInfo?.scalabilityModes,
                width: targetSize.width,
                height: targetSize.height,
                bitrate: targetBitrate,
                framerate: targetFramerate,
                cameraDeviceId: this.selectedCameraDeviceId ?? undefined,
                backgroundBlur: {
                    enabled: this.isBlurEnabled,
                },
                // Enable streaming to server for real-time viewing
                streaming: {
                    enabled: true,
                    chatId: this.chatId,
                },
                // Enable VAD-based adaptive framerate to reduce bandwidth when not speaking
                adaptiveFramerate: {
                    enabled: true,
                },
            };

            this.recordingService = this.createRecordingService(config);

            // Start recording (this initializes the video-pipeline)
            await this.recordingService.start();

            this.previewTrack = this.recordingService.getInputTrack()
            // Store actual camera resolution for capping reconfigure requests
            const trackSettings = this.previewTrack!.getSettings();
            infoLog?.log(`Track resolution: ${trackSettings.width}x${trackSettings.height}`);
            this.cameraWidth = trackSettings.width ?? config.width;
            this.cameraHeight = trackSettings.height ?? config.height;
            infoLog?.log(`Camera resolution: ${this.cameraWidth}x${this.cameraHeight}`);

            // Subscribe to VAD for adaptive framerate
            this.recordingService.getPipeline()?.subscribeToVad();

            // Set preview callback so blurred frames are forwarded to the external handler
            this.recordingService.setPreviewCallback((frame: VideoFrame) => {
                if (this.isBlurEnabled && this.onPreviewFrame)
                    this.onPreviewFrame(frame);
            });

            this.isRecording = true;
            activeRecorderInstance = this; // eslint-disable-line @typescript-eslint/no-this-alias

            // Notify Blazor that recording started successfully
            await this.blazorRef.invokeMethodAsync('OnRecordingStarted');

            infoLog?.log('Video recording started');
        } catch (error) {
            errorLog?.log('Failed to start recording:', error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', String(error));
        }
    }

    /**
     * Start screencast (screen sharing) recording
     */
    public async startScreencast(chatId: string, audienceCodecs?: string[]): Promise<void> {
        this.chatId = chatId;
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        infoLog?.log('Starting screencast...');

        try {
            // Detect supported encoder codecs — use mobile-aware resolution to avoid
            // false negatives (e.g. Android HEVC encoders may not support 1080p)
            const detectionWidth = DeviceInfo.isMobile ? 1280 : 1920;
            const detectionHeight = DeviceInfo.isMobile ? 720 : 1080;
            const supportedCodecs = await detectSupportedCodecs(detectionWidth, detectionHeight);
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);

            // Pick initial codec based on audience
            const targetSize = { width: 1920, height: 1080 };
            const bestCodecString = this.pickInitialCodec(supportedCodecs, audienceCodecs, targetSize);
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);
            const codecCategory = getCodecCategory(bestCodecString);

            // Screencast config: start at 1080p cap, quality preset will adjust
            const config: RecordingConfig = {
                mode: 'screen',
                codec: codecCategory,
                codecString: bestCodecString,
                hardwareAccelerated: bestCodecInfo?.hardwareAccelerated ?? false,
                scalabilityModes: bestCodecInfo?.scalabilityModes,
                width: targetSize.width,
                height: targetSize.height,
                bitrate: 4_000_000, // Start at High quality (not Full 8Mbps at 4K)
                framerate: 30,
                backgroundBlur: { enabled: false },
                streaming: {
                    enabled: true,
                    chatId: this.chatId,
                },
                adaptiveFramerate: {
                    enabled: true,
                },
            };

            this.recordingService = this.createRecordingService(config);

            // Start recording — getDisplayMedia will prompt the user to pick a screen
            await this.recordingService.start();

            // Get the screen track for preview and track-ended detection
            const pipeline = this.recordingService.getPipeline();
            const screenTrack = this.recordingService.getInputTrack();
            if (screenTrack) {
                // Use screen track for local preview
                this.previewTrack = screenTrack;

                // Store actual screen resolution for capping reconfigure requests
                const trackSettings = screenTrack.getSettings();
                this.cameraWidth = trackSettings.width ?? targetSize.width;
                this.cameraHeight = trackSettings.height ?? targetSize.height;
                infoLog?.log(`Screen resolution: ${this.cameraWidth}x${this.cameraHeight}`);

                // Handle browser's native "Stop sharing" button
                screenTrack.onended = () => {
                    infoLog?.log('Screen sharing track ended (user stopped sharing)');
                    void this.stopRecording();
                };
            }

            // Subscribe to VAD for adaptive framerate
            pipeline?.subscribeToVad();

            this.isRecording = true;
            this.isScreencasting = true;
            activeRecorderInstance = this; // eslint-disable-line @typescript-eslint/no-this-alias

            await this.blazorRef.invokeMethodAsync('OnRecordingStarted');
            infoLog?.log('Screencast started');
        } catch (error) {
            errorLog?.log('Failed to start screencast:', error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', String(error));
        }
    }

    /**
     * Stop video recording
     */
    public async stopRecording(): Promise<void> {
        if (!this.isRecording || !this.recordingService) {
            return;
        }

        infoLog?.log('Stopping video recording...');

        try {
            await this.recordingService.stop();
            this.cleanupPreviewTrack();
            this.isRecording = false;
            this.isScreencasting = false;
            if (activeRecorderInstance === this) activeRecorderInstance = null;

            // Notify Blazor
            await this.blazorRef.invokeMethodAsync('OnRecordingStopped');

            infoLog?.log('Video recording stopped');
        } catch (error) {
            errorLog?.log('Failed to stop recording:', error);
        }
    }

    /**
     * Update the list of decoder codecs supported by all receivers.
     * The sender picks the best codec it can actually encode from this list.
     * Called from Blazor when the server pushes updated decoder capabilities.
     */
    public async updateSupportedDecoderCodecs(codecs: string[]): Promise<void> {
        if (!this.recordingService) return;

        // Filter server's list by sender's encoder capabilities
        const matchingCategories = codecs.filter(c => this.supportedEncoderCategories.includes(c));

        if (matchingCategories.length === 0) {
            warnLog?.log(`updateSupportedDecoderCodecs: no match between server codecs [${codecs.join(', ')}] and encoder capabilities [${this.supportedEncoderCategories.join(', ')}], keeping current codec`);
            return;
        }

        // Use getDefaultCodec() for HW-aware selection (same logic as initial codec pick)
        const audienceFilteredCodecs = this.supportedCodecs.filter(c =>
            c.supported && matchingCategories.includes(c.category)
        );
        if (audienceFilteredCodecs.length === 0) return;

        const pickedCodecString = getDefaultCodec(audienceFilteredCodecs, this.cameraWidth || 1280, this.cameraHeight || 720);
        const pickedCategory = getCodecCategory(pickedCodecString);

        infoLog?.log(`Selected encoder codec: ${pickedCategory} from supported decoders: [${codecs.join(', ')}]`);
        await this.recordingService.switchCodec(pickedCategory);
    }

    /**
     * Reconfigure encoder bitrate/resolution (called from Blazor quality subscription)
     */
    public reconfigure(level: string, width: number, height: number, bitrate: number): void {
        if (!this.recordingService) {
            warnLog?.log('reconfigure: no active recording service');
            return;
        }

        const pipeline = this.recordingService.getPipeline();
        if (!pipeline) return;

        // Handle server-driven pause
        if (level === 'Paused') {
            infoLog?.log('reconfigure: server paused this stream');
            pipeline.pauseEncoding();
            return;
        }

        // Resume if we were paused
        pipeline.resumeEncoding();

        // Transpose preset if camera orientation doesn't match (e.g., portrait camera, landscape preset)
        infoLog?.log(`reconfigure: level=${level}, size=${width}x${height}, bitrate=${bitrate}, cameraSize=${this.cameraWidth}x${this.cameraHeight}`, );
        const cameraIsPortrait = this.cameraWidth > 0 && this.cameraHeight > 0 && this.cameraHeight > this.cameraWidth;
        const presetIsLandscape = width > height;
        if (cameraIsPortrait && presetIsLandscape)
            [width, height] = [height, width];

        // Cap to actual camera resolution — upscaling wastes CPU for no quality gain
        const cappedWidth = this.cameraWidth > 0 ? Math.min(width, this.cameraWidth) : width;
        const cappedHeight = this.cameraHeight > 0 ? Math.min(height, this.cameraHeight) : height;

        // Cap bitrate for mobile and low-power devices
        let cappedBitrate = bitrate;
        if (DeviceInfo.isIos) {
            cappedBitrate = Math.min(cappedBitrate, 1_000_000);
        } else if (DeviceInfo.isMobile) {
            cappedBitrate = Math.min(cappedBitrate, 2_000_000);
        }

        if (cappedWidth !== width || cappedHeight !== height || cappedBitrate !== bitrate)
            infoLog?.log(`reconfigure: ${width}x${height} @ ${bitrate / 1_000_000}Mbps → capped to ${cappedWidth}x${cappedHeight} @ ${cappedBitrate / 1_000_000}Mbps`);
        else
            infoLog?.log(`reconfigure: ${width}x${height} @ ${bitrate / 1_000_000}Mbps`);

        void pipeline.reconfigure({ bitrate: cappedBitrate, width: cappedWidth, height: cappedHeight });
    }

    public forceKeyFrame(): void {
        const pipeline = this.recordingService?.getPipeline();
        if (!pipeline) {
            warnLog?.log('forceKeyFrame: no active pipeline');
            return;
        }
        infoLog?.log('forceKeyFrame: PLI — forcing keyframe on encoder');
        void pipeline.forceKeyFrame();
    }

    private pickInitialCodec(supportedCodecs: CodecInfo[], audienceCodecs: string[] | undefined, size: Size) {
        if (audienceCodecs && audienceCodecs.length > 0) {
            const matchingCategories = audienceCodecs.filter(c => this.supportedEncoderCategories.includes(c));
            if (matchingCategories.length > 0) {
                const audienceFilteredCodecs = supportedCodecs.filter(c =>
                    c.supported && matchingCategories.includes(c.category),
                );
                return audienceFilteredCodecs.length > 0
                    ? getDefaultCodec(audienceFilteredCodecs, size.width, size.height)
                    : getDefaultCodec(supportedCodecs, size.width, size.height);
            } else {
                return getDefaultCodec(supportedCodecs, size.width, size.height);
            }
        } else {
            return getDefaultCodec(supportedCodecs, size.width, size.height);
        }
    }

    private createRecordingService(config: RecordingConfig): RecordingService {
        const recordingService = new RecordingService(config);
        recordingService.addEventListener('state-change', ((event: CustomEvent<RecordingState>) => {
            this.onRecorderStateChange(event.detail);
        }) as EventListener);
        recordingService.addEventListener('error', ((event: CustomEvent<Error>) => {
            this.onRecorderError(event.detail);
        }) as EventListener);
        return recordingService;
    }

    private cleanupPreviewTrack(): void {
        if (this.previewTrack) {
            // For screencast, don't stop the track — it's shared with the pipeline.
            // The pipeline's stop() will handle track cleanup.
            if (!this.isScreencasting)
                this.previewTrack.stop();
            this.previewTrack = null;
        }
    }

    /**
     * Handle recorder state changes
     */
    private onRecorderStateChange(state: RecordingState): void {
        // Skip pure duration ticks — only act on actual status transitions
        if (state.status === this.lastStatus)
            return;
        this.lastStatus = state.status;

        debugLog?.log('Recorder state changed:', state);
    }

    /**
     * Handle recorder errors
     */
    private onRecorderError(error: Error): void {
        errorLog?.log('Recorder error:', error);
        void this.blazorRef.invokeMethodAsync('OnRecordingError', error.message);
    }

    /**
     * Extract unique encoder codec categories from detected codec support.
     * Returns categories like ['av1', 'h264'] based on what the encoder can actually produce.
     */
    private extractEncoderCategories(codecs: CodecInfo[]): string[] {
        const categories = new Set<string>();
        for (const c of codecs) {
            if (c.supported) {
                // AV1 software encoding is too expensive for real-time — require HW
                if (c.category === 'av1' && !c.hardwareAccelerated) continue;
                // On mobile, SW encoding is too CPU-intensive for anything except H264
                // (VP9-SW on Android silently drops all frames, HEVC-SW is equally broken)
                if (DeviceInfo.isMobile && !c.hardwareAccelerated && c.category !== 'h264') continue;
                categories.add(c.category);
            }
        }
        // Return in priority order: av1, hevc, vp9, h264
        const ordered: string[] = [];
        if (categories.has('av1')) ordered.push('av1');
        if (categories.has('hevc')) ordered.push('hevc');
        if (categories.has('vp9')) ordered.push('vp9');
        if (categories.has('h264')) ordered.push('h264');
        return ordered;
    }

    public getDiagnostics(): OwnStreamDiagnostics {
        const rs = this.recordingService;
        const pipeline = rs?.getPipeline();
        const encoderStats = pipeline?.getEncoderStats();
        const segStats = pipeline?.getSegmentationStats();
        const state = rs?.getState();
        const config = rs?.getConfig();
        const inputTrack = rs?.getInputTrack();
        const trackSettings = inputTrack?.getSettings();

        const duration = state?.duration ?? 0;
        const actualBitrateKbps = duration > 0 && encoderStats
            ? (encoderStats.totalBytes * 8) / duration / 1000
            : 0;

        return {
            mode: this.isScreencasting ? 'screen' : this.isRecording ? 'webcam' : 'none',
            codec: config?.codecString ?? '',
            codecCategory: config?.codecString ? getCodecCategory(config.codecString) : '',
            hardwareAccelerated: config?.hardwareAccelerated ?? false,
            inputResolution: trackSettings ? `${trackSettings.width ?? 0}x${trackSettings.height ?? 0}` : 'N/A',
            inputFramerate: trackSettings?.frameRate ?? 0,
            outputResolution: encoderStats
                ? `${encoderStats.configuredWidth}x${encoderStats.configuredHeight}`
                : 'N/A',
            configuredBitrate: encoderStats?.configuredBitrate ?? 0,
            actualBitrateKbps: Math.round(actualBitrateKbps),
            encodedFrames: encoderStats?.encodedFrames ?? 0,
            droppedFrames: encoderStats?.droppedFrames ?? 0,
            keyFrames: encoderStats?.keyFrames ?? 0,
            medianEncodeTime: encoderStats?.medianEncodeTime ?? 0,
            pureMedianEncodeTime: encoderStats?.pureMedianEncodeTime ?? 0,
            encoderHwAccel: encoderStats?.hardwareAcceleration ?? 'unknown',
            duration,
            cameraLabel: inputTrack?.label ?? null,
            blurEnabled: this.isBlurEnabled,
            segmentationBackend: segStats?.backend ?? null,
            segmentationAvgTime: segStats?.averageTotalTime ?? null,
            supportedEncoderCategories: this.supportedEncoderCategories,
            status: state?.status ?? 'idle',
        };
    }

    public dispose() {
        if (this.disposed)
            return;
        this.disposed = true;
        if (activeRecorderInstance === this) activeRecorderInstance = null;

        this.cleanupPreviewTrack();

        // Stop recording service
        if (this.recordingService) {
            void this.recordingService.stop();
            this.recordingService = null;
        }

        this.onPreviewFrame = null;
        this.isRecording = false;
        this.isScreencasting = false;
    }
}

import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { RecordingService, type RecordingConfig, type RecordingState } from '../../Services/Video/services/recording-service';
import { detectSupportedCodecs, getDefaultCodec, getCodecCategory, type CodecInfo } from '../../Services/Video/codec-support';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoRecorder');

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
    orientation: {
        firstDisplayResolution: string;
        firstCodedResolution: string;
        firstRotation: string;
        lastRotation: string;
        configuredResolution: string;
        needsRotation: boolean;
        rotationDetection: string;
        framesSeen: number;
    } | null;
}

export interface VideoDevice {
    deviceId: string;
    label: string;
}

// Module-level registry keyed by StreamKind so a user can simultaneously
// stream webcam (kind=0) and screencast (kind=1). Callers that want the
// webcam-specific recorder (preview, modal, diagnostics) pass kind=0
// (the default). Kinds match the C# StreamKind enum values.
const StreamKindWebcam = 0;
const StreamKindScreencast = 1;
const activeRecorders = new Map<number, VideoRecorder>();

export function getActiveRecorder(kind: number = StreamKindWebcam): VideoRecorder | null {
    return activeRecorders.get(kind) ?? null;
}

export function getAllActiveRecorders(): VideoRecorder[] {
    return [...activeRecorders.values()];
}

interface Size {
    width: number;
    height: number;
}

/**
 * See {@link VideoRecorder.addPreviewFrameListener} for the listener contract.
 */
export type PreviewFrameListener = (frame: VideoFrame) => void;

export class VideoRecorder {
    private blazorRef: DotNet.DotNetObject;
    // Video recording service (using video-pipeline)
    private recordingService: RecordingService | null = null;
    private isRecording = false;
    // True when we were asked to record but currently have no active pipeline
    // (e.g. the user switched to a camera that failed to start). The next
    // switchCamera call restarts from this state.
    private isInterrupted = false;
    private isScreencasting = false;
    // StreamKind this instance is registered under. Set in the constructor and
    // cleared on dispose (we register immediately so VideoStreamingPreview can
    // see the recorder during the pipeline startup phase, not only after the
    // first frame lands).
    private registeredKind: number | null = null;
    private previewTrack: MediaStreamTrack | null = null;
    private selectedCameraDeviceId: string | null = null;
    private chatId = '';
    private isBlurEnabled = false;
    private blurToggleChain: Promise<void> = Promise.resolve();
    private disposed = false;
    private lastStatus = '';
    private cameraWidth = 0;
    private cameraHeight = 0;
    // Cached encoder capabilities (detected at recording start)
    private supportedEncoderCategories: string[] = [];
    private supportedCodecs: CodecInfo[] = [];

    // Blur preview frame subscribers. When blur is active, the pipeline produces
    // `VideoFrame`s that we dispatch to every listener before the frame is closed
    // by the pipeline. Listeners MUST consume the frame synchronously (draw it to
    // a canvas before returning); they must NOT close it and must NOT retain it.
    private previewFrameListeners = new Set<PreviewFrameListener>();

    // When true, RecorderPreviewView instances configured to respect this flag
    // freeze their current frame instead of tracking the recorder. Used when a
    // modal (JoinVideoCallModal in Settings mode) takes over preview rendering
    // so the VideoPanel's self-preview doesn't double-render alongside it.
    private previewPaused = false;

    static create(blazorRef: DotNet.DotNetObject, kind: number): VideoRecorder {
        return new VideoRecorder(blazorRef, kind);
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

    constructor(blazorRef: DotNet.DotNetObject, kind: number) {
        this.blazorRef = blazorRef;
        this.register(kind);
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
     *
     * If the new camera fails to start (e.g. a ghost device registered in the OS but not
     * delivering frames), startRecording's error path leaves us in the interrupted state
     * (`isInterrupted = true`, `recordingService = null`). The Blazor side no longer
     * tears the panel down on OnRecordingError, so the user can simply click switch
     * camera again — the next call will fall through the stop branch (pipeline is
     * already null) and try startRecording with the new device.
     */
    public async switchCamera(deviceId: string): Promise<void> {
        this.selectedCameraDeviceId = deviceId;
        infoLog?.log('Switching camera to:', deviceId);

        // Never asked to record yet — just remember the device for the next start
        if (!this.chatId) {
            infoLog?.log('Not yet recording — camera will be used on next start');
            return;
        }

        // Tear down the current pipeline if one exists
        if (this.recordingService) {
            this.cleanupPreviewTrack();
            try {
                await this.recordingService.stop();
            } catch (e) {
                warnLog?.log('Stop during switch failed:', e);
            }
            this.recordingService = null;
            this.isRecording = false;
        }

        await this.startRecording(this.chatId);
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
     * Whether recording was requested but the current pipeline failed to start.
     * A subsequent switchCamera call will try to start fresh with the new device.
     */
    public isRecordingInterrupted(): boolean {
        return this.isInterrupted;
    }

    /**
     * Subscribe to blur preview frames produced by the recorder's pipeline.
     * The returned function unsubscribes the listener.
     *
     * Listener contract:
     *  - called synchronously from the pipeline, once per produced frame;
     *  - MUST consume the frame within the callback (e.g. `ctx.drawImage(frame, ...)`);
     *  - MUST NOT call `frame.close()` (ownership stays with the pipeline);
     *  - MUST NOT retain the frame (the pipeline closes it after dispatch);
     *  - only fires while `isBlurActive()` is true.
     */
    public addPreviewFrameListener(cb: PreviewFrameListener): () => void {
        this.previewFrameListeners.add(cb);
        return () => this.previewFrameListeners.delete(cb);
    }

    /** Whether preview rendering is paused (another consumer owns the canvas). */
    public isPreviewPaused(): boolean {
        return this.previewPaused;
    }

    /** Pause preview rendering for consumers that respect the flag. */
    public pausePreviewRendering(): void {
        this.previewPaused = true;
    }

    /** Resume preview rendering. */
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

        this.isInterrupted = false;
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

            // Fan out blur preview frames to every subscriber. Each listener draws
            // synchronously; the pipeline closes the frame immediately after this
            // callback returns (see `video-pipeline.ts`).
            this.recordingService.setPreviewCallback((frame: VideoFrame) => {
                if (!this.isBlurEnabled) return;
                for (const listener of this.previewFrameListeners) {
                    try {
                        listener(frame);
                    } catch (e) {
                        warnLog?.log('preview frame listener threw', e);
                    }
                }
            });

            this.isRecording = true;

            // Notify Blazor that recording started successfully
            await this.blazorRef.invokeMethodAsync('OnRecordingStarted');

            infoLog?.log('Video recording started');
        } catch (error) {
            this.isInterrupted = true;
            errorLog?.log('Failed to start recording:', error);
            const message = error instanceof Error ? error.message : String(error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', message);
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

            await this.blazorRef.invokeMethodAsync('OnRecordingStarted');
            infoLog?.log('Screencast started');
        } catch (error) {
            errorLog?.log('Failed to start screencast:', error);
            const message = error instanceof Error ? error.message : String(error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', message);
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
            this.unregister();

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
        recordingService.addEventListener('encoder-failure', ((event: CustomEvent<string>) => {
            this.onEncoderCodecFailed(event.detail);
        }) as EventListener);
        return recordingService;
    }

    /** Remove failed encoder codec so updateSupportedDecoderCodecs won't pick it again */
    private onEncoderCodecFailed(category: string): void {
        const idx = this.supportedEncoderCategories.indexOf(category);
        if (idx >= 0) {
            this.supportedEncoderCategories.splice(idx, 1);
            warnLog?.log(`Excluded encoder codec '${category}' after failure. Remaining: [${this.supportedEncoderCategories.join(', ')}]`);
        }
    }

    private register(kind: number): void {
        this.registeredKind = kind;
        activeRecorders.set(kind, this);
    }

    private unregister(): void {
        if (this.registeredKind !== null && activeRecorders.get(this.registeredKind) === this) {
            activeRecorders.delete(this.registeredKind);
        }
        this.registeredKind = null;
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
        const orientStats = pipeline?.getOrientationStats();
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
            orientation: orientStats ? {
                firstDisplayResolution: `${orientStats.firstDisplayWidth}x${orientStats.firstDisplayHeight}`,
                firstCodedResolution: `${orientStats.firstCodedWidth}x${orientStats.firstCodedHeight}`,
                firstRotation: orientStats.firstRotation !== null ? `${orientStats.firstRotation}°` : 'N/A',
                lastRotation: orientStats.lastRotation !== null ? `${orientStats.lastRotation}°` : 'N/A',
                configuredResolution: `${orientStats.configuredWidth}x${orientStats.configuredHeight}`,
                needsRotation: orientStats.needsRotation,
                rotationDetection: orientStats.rotationDetection,
                framesSeen: orientStats.framesSeen,
            } : null,
        };
    }

    public dispose() {
        if (this.disposed)
            return;
        this.disposed = true;
        this.unregister();

        // Drop listeners before tearing down the pipeline so no in-flight
        // preview callback reaches a listener after we're gone.
        this.previewFrameListeners.clear();

        this.cleanupPreviewTrack();

        // Stop recording service
        if (this.recordingService) {
            void this.recordingService.stop();
            this.recordingService = null;
        }

        this.isRecording = false;
        this.isScreencasting = false;
    }
}

import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { RecordingService, type RecordingConfig, type RecordingState } from '../../Services/Video/services/recording-service';
import { detectSupportedCodecs, getDefaultCodec, getCodecCategory, type CodecInfo } from '../../Services/Video/codec-support';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoRecorder');

export interface VideoDevice {
    deviceId: string;
    label: string;
}

// Module-level singleton so the modal can find the active recorder
let activeRecorderInstance: VideoRecorder | null = null;
export function getActiveRecorder(): VideoRecorder | null {
    return activeRecorderInstance;
}

export class VideoRecorder {
    private blazorRef: DotNet.DotNetObject;
    private readonly element: HTMLElement;
    private readonly canvas: HTMLCanvasElement | null = null;
    private readonly canvasCtx: CanvasRenderingContext2D | null = null;
    // Video recording service (using video-pipeline)
    private recordingService: RecordingService | null = null;
    private isRecording = false;
    private isScreencasting = false;
    private animationFrameId: number | null = null;
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
    // Cached encoder capabilities (detected at 1080p at recording start)
    private supportedEncoderCategories: string[] = [];
    private supportedCodecs: CodecInfo[] = [];

    static create(element: HTMLElement, blazorRef: DotNet.DotNetObject): VideoRecorder {
        return new VideoRecorder(element, blazorRef);
    }

    constructor(element: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.element = element;

        // Get canvas element for rendering
        this.canvas = this.element.querySelector('.call-video')!;
        this.canvasCtx = this.canvas.getContext('2d');
    }

    /**
     * Enumerate available video devices
     */
    public async enumerateVideoDevices(): Promise<VideoDevice[]> {
        try {
            // Request permission if not already granted
            await navigator.mediaDevices.getUserMedia({ video: true, audio: false });

            const devices = await navigator.mediaDevices.enumerateDevices();
            const videoDevices = devices
                .filter(device => device.kind === 'videoinput')
                .map(device => ({
                    deviceId: device.deviceId,
                    label: device.label || `Camera ${device.deviceId.slice(0, 8)}`
                }));

            infoLog?.log('Enumerated video devices:', videoDevices);
            return videoDevices;
        } catch (error) {
            errorLog?.log('Failed to enumerate video devices:', error);
            return [];
        }
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
            this.stopRenderingStream();
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
     * Pause the recorder's own preview rendering (so only the modal draws while it's open).
     */
    public pausePreviewRendering(): void {
        this.previewPaused = true;
    }

    /**
     * Resume the recorder's own preview rendering.
     */
    public resumePreviewRendering(): void {
        this.previewPaused = false;
    }

    /**
     * Initialize and start video recording
     */
    public async startRecording(chatId: string, audienceCodecs?: string[]): Promise<void> {
        this.chatId = chatId;
        console.warn('[VideoRecorder] startRecording called, isRecording:', this.isRecording, 'chatId:', this.chatId);
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        infoLog?.log('Starting video recording...');

        try {
            // Capture at 720p on all platforms — lower resolutions may select the wrong
            // camera on Android and produce aspect-ratio mismatches.
            const targetWidth = 1280;
            const targetHeight = 720;
            const targetBitrate = 2_000_000;
            const targetFramerate = 30;

            // Detect supported encoder codecs — use target resolution to avoid
            // false negatives on mobile (e.g. Android HEVC encoders may not support 1080p)
            const supportedCodecs = await detectSupportedCodecs(targetWidth, targetHeight);
            this.supportedCodecs = supportedCodecs;

            // Cache supported encoder categories for later codec negotiation
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);
            infoLog?.log(`Supported encoder categories: [${this.supportedEncoderCategories.join(', ')}]`);

            // Pick initial codec: if audience codecs are known, pick the best encoder
            // codec that the audience can decode — avoids mid-stream codec switches
            let bestCodecString: string;
            if (audienceCodecs && audienceCodecs.length > 0) {
                infoLog?.log(`Audience decoder codecs: [${audienceCodecs.join(', ')}]`);
                const matchingCategories = audienceCodecs.filter(c => this.supportedEncoderCategories.includes(c));
                if (matchingCategories.length > 0) {
                    // Filter supported codecs to only those in audience-compatible categories
                    const audienceFilteredCodecs = supportedCodecs.filter(c =>
                        c.supported && matchingCategories.includes(c.category)
                    );
                    bestCodecString = audienceFilteredCodecs.length > 0
                        ? getDefaultCodec(audienceFilteredCodecs, targetWidth, targetHeight)
                        : getDefaultCodec(supportedCodecs, targetWidth, targetHeight);
                } else {
                    bestCodecString = getDefaultCodec(supportedCodecs, targetWidth, targetHeight);
                }
            } else {
                bestCodecString = getDefaultCodec(supportedCodecs, targetWidth, targetHeight);
            }
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
                width: targetWidth,
                height: targetHeight,
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

            console.warn('[VideoRecorder] Creating RecordingService with streaming:', config.streaming);
            this.recordingService = new RecordingService(config);

            // Listen for state changes
            this.recordingService.addEventListener('state-change', ((event: CustomEvent<RecordingState>) => {
                this.onRecorderStateChange(event.detail);
            }) as EventListener);

            // Listen for errors
            this.recordingService.addEventListener('error', ((event: CustomEvent<Error>) => {
                this.onRecorderError(event.detail);
            }) as EventListener);

            // Acquire a separate camera stream for local preview.
            // We must get this BEFORE starting the recording pipeline, because
            // the pipeline's MediaStreamTrackProcessor exclusively consumes
            // frames from the track it's given — cloning after that point
            // produces a dead track in Chromium.
            const previewConstraints: MediaStreamConstraints = {
                video: this.selectedCameraDeviceId
                    ? { deviceId: { exact: this.selectedCameraDeviceId }, width: { ideal: targetWidth }, height: { ideal: targetHeight }, frameRate: { ideal: targetFramerate } }
                    : { width: { ideal: targetWidth }, height: { ideal: targetHeight }, frameRate: { ideal: targetFramerate } },
                audio: false,
            };
            const previewStream = await navigator.mediaDevices.getUserMedia(previewConstraints);
            this.previewTrack = previewStream.getVideoTracks()[0];

            // Store actual camera resolution for capping reconfigure requests
            const trackSettings = this.previewTrack.getSettings();
            this.cameraWidth = trackSettings.width ?? config.width;
            this.cameraHeight = trackSettings.height ?? config.height;
            infoLog?.log(`Camera resolution: ${this.cameraWidth}x${this.cameraHeight}`);

            // Ensure recording uses the same camera as preview
            const previewDeviceId = trackSettings.deviceId;
            if (previewDeviceId && !this.selectedCameraDeviceId) {
                infoLog?.log(`Captured preview camera device ID: ${previewDeviceId}`);
                this.selectedCameraDeviceId = previewDeviceId;
                this.recordingService.updateConfig({ cameraDeviceId: previewDeviceId });
            }

            // Start recording (this initializes the video-pipeline)
            console.warn('[VideoRecorder] Calling recordingService.start()...');
            await this.recordingService.start();
            console.warn('[VideoRecorder] recordingService.start() completed successfully');

            // Subscribe to VAD for adaptive framerate
            this.recordingService.getPipeline()?.subscribeToVad();

            // Set preview callback so blurred frames render to the preview canvas
            this.recordingService.setPreviewCallback((frame: VideoFrame) => {
                if (this.isBlurEnabled && this.canvas && this.canvasCtx) {
                    if (this.canvas.width !== frame.displayWidth || this.canvas.height !== frame.displayHeight) {
                        if (frame.displayWidth > 0 && frame.displayHeight > 0) {
                            this.canvas.width = frame.displayWidth;
                            this.canvas.height = frame.displayHeight;
                        }
                    }
                    this.canvasCtx.drawImage(frame, 0, 0);
                }
            });

            // Set isRecording BEFORE starting the render loop — the render loop
            // checks this flag and exits permanently if it's false on the first frame.
            this.isRecording = true;
            activeRecorderInstance = this; // eslint-disable-line @typescript-eslint/no-this-alias

            // Start rendering preview AFTER isRecording is set
            console.warn('[VideoRecorder] Starting preview rendering, canvas:', !!this.canvas, 'canvasCtx:', !!this.canvasCtx);
            this.startRenderingStream(previewStream);

            // Notify Blazor
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
            let bestCodecString: string;
            if (audienceCodecs && audienceCodecs.length > 0) {
                const matchingCategories = audienceCodecs.filter(c => this.supportedEncoderCategories.includes(c));
                if (matchingCategories.length > 0) {
                    const audienceFilteredCodecs = supportedCodecs.filter(c =>
                        c.supported && matchingCategories.includes(c.category)
                    );
                    bestCodecString = audienceFilteredCodecs.length > 0
                        ? getDefaultCodec(audienceFilteredCodecs, 1920, 1080)
                        : getDefaultCodec(supportedCodecs, 1920, 1080);
                } else {
                    bestCodecString = getDefaultCodec(supportedCodecs, 1920, 1080);
                }
            } else {
                bestCodecString = getDefaultCodec(supportedCodecs, 1920, 1080);
            }
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);
            const codecCategory = getCodecCategory(bestCodecString);

            // Screencast config: start at 1080p cap, quality preset will adjust
            const config: RecordingConfig = {
                mode: 'screen',
                codec: codecCategory,
                codecString: bestCodecString,
                hardwareAccelerated: bestCodecInfo?.hardwareAccelerated ?? false,
                scalabilityModes: bestCodecInfo?.scalabilityModes,
                width: 1920,
                height: 1080,
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

            this.recordingService = new RecordingService(config);

            this.recordingService.addEventListener('state-change', ((event: CustomEvent<RecordingState>) => {
                this.onRecorderStateChange(event.detail);
            }) as EventListener);

            this.recordingService.addEventListener('error', ((event: CustomEvent<Error>) => {
                this.onRecorderError(event.detail);
            }) as EventListener);

            // Start recording — getDisplayMedia will prompt the user to pick a screen
            await this.recordingService.start();

            // Get the screen track for preview and track-ended detection
            const pipeline = this.recordingService.getPipeline();
            const screenStream = this.recordingService.getInputStream();
            if (screenStream) {
                const screenTrack = screenStream.getVideoTracks()[0];
                // Use screen track for local preview
                this.previewTrack = screenTrack;

                // Store actual screen resolution for capping reconfigure requests
                const trackSettings = screenTrack.getSettings();
                this.cameraWidth = trackSettings.width ?? 1920;
                this.cameraHeight = trackSettings.height ?? 1080;
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

            // Start rendering preview from the screen stream
            if (screenStream) {
                this.startRenderingStream(screenStream);
            }

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
            this.stopRenderingStream();
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

        // Transpose preset if camera orientation doesn't match (e.g., portrait camera, landscape preset)
        const cameraIsPortrait = this.cameraWidth > 0 && this.cameraHeight > 0 && this.cameraHeight > this.cameraWidth;
        const presetIsLandscape = width > height;
        if (cameraIsPortrait && presetIsLandscape)
            [width, height] = [height, width];

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

    /**
     * Start rendering the output stream to canvas
     */
    private startRenderingStream(stream: MediaStream): void {
        const videoTrack = stream.getVideoTracks()[0];
        console.warn('[VideoRecorder] startRenderingStream: videoTrack:', !!videoTrack, 'canvas:', !!this.canvas, 'canvasCtx:', !!this.canvasCtx);

        // Create a video element to render the stream
        const video = document.createElement('video');
        video.srcObject = stream;
        video.muted = true;
        video.playsInline = true;
        void video.play();

        let frameCount = 0;
        let hasVideo = false;
        const renderFrame = () => {
            if (!this.isRecording || !this.canvas || !this.canvasCtx) {
                console.warn('[VideoRecorder] renderFrame: exiting loop, isRecording:', this.isRecording);
                return;
            }
            frameCount++;
            if (frameCount <= 3 || frameCount % 300 === 0)
                console.warn(`[VideoRecorder] renderFrame #${frameCount}: videoWidth=${String(video.videoWidth)}, videoHeight=${String(video.videoHeight)}`);

            // Mark first frame received — hides loading spinner
            if (!hasVideo && video.videoWidth > 0 && video.videoHeight > 0) {
                hasVideo = true;
                this.element.classList.add('has-video');
            }

            // When preview is paused (modal is open), skip drawing but keep the loop alive.
            if (this.previewPaused) {
                this.animationFrameId = requestAnimationFrame(renderFrame);
                return;
            }

            // When blur is enabled, the preview callback renders blurred frames directly.
            // Only draw the raw camera feed when blur is off.
            if (!this.isBlurEnabled) {
                // Resize canvas if needed
                if (this.canvas.width !== video.videoWidth || this.canvas.height !== video.videoHeight) {
                    if (video.videoWidth > 0 && video.videoHeight > 0) {
                        this.canvas.width = video.videoWidth;
                        this.canvas.height = video.videoHeight;
                    }
                }

                // Draw frame to canvas
                if (video.videoWidth > 0 && video.videoHeight > 0) {
                    this.canvasCtx.drawImage(video, 0, 0);
                }
            }

            this.animationFrameId = requestAnimationFrame(renderFrame);
        };

        this.animationFrameId = requestAnimationFrame(renderFrame);
    }

    /**
     * Stop rendering the stream
     */
    private stopRenderingStream(): void {
        if (this.animationFrameId !== null) {
            cancelAnimationFrame(this.animationFrameId);
            this.animationFrameId = null;
        }
        if (this.previewTrack) {
            // For screencast, don't stop the track — it's shared with the pipeline.
            // The pipeline's stop() will handle track cleanup.
            if (!this.isScreencasting)
                this.previewTrack.stop();
            this.previewTrack = null;
        }
        this.element.classList.remove('has-video');
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

        // Update UI based on state
        if (state.status.startsWith('error')) {
            this.element.classList.add('has-error');
        } else {
            this.element.classList.remove('has-error');
        }

        // Notify Blazor of state change
        void this.blazorRef.invokeMethodAsync('OnRecorderStateChanged', JSON.stringify(state));
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

    public dispose() {
        if (this.disposed)
            return;
        this.disposed = true;
        if (activeRecorderInstance === this) activeRecorderInstance = null;

        // Stop rendering
        this.stopRenderingStream();

        // Stop recording service
        if (this.recordingService) {
            void this.recordingService.stop();
            this.recordingService = null;
        }

        this.isRecording = false;
        this.isScreencasting = false;
    }
}

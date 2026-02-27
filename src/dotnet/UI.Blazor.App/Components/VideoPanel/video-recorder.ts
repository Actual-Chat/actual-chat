import { Log } from 'logging';
import { RecordingService, type RecordingConfig, type RecordingState } from '../../Services/Video/services/recording-service';
import { detectSupportedCodecs, getDefaultCodec, type CodecInfo } from '../../Services/Video/codec-support';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoRecorder');

export interface VideoDevice {
    deviceId: string;
    label: string;
}

export class VideoRecorder {
    private blazorRef: DotNet.DotNetObject;
    private readonly element: HTMLElement;
    private readonly canvas: HTMLCanvasElement | null = null;
    private readonly canvasCtx: CanvasRenderingContext2D | null = null;
    // Video recording service (using video-pipeline)
    private recordingService: RecordingService | null = null;
    private isRecording = false;
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
    // Cached encoder capabilities (detected at 1080p at recording start)
    private supportedEncoderCategories: string[] = [];

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
     * Initialize and start video recording
     */
    public async startRecording(chatId: string): Promise<void> {
        this.chatId = chatId;
        console.warn('[VideoRecorder] startRecording called, isRecording:', this.isRecording, 'chatId:', this.chatId);
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        infoLog?.log('Starting video recording...');

        try {
            // Detect supported encoder codecs at 1080p (avoids resolution-dependent false positives)
            const supportedCodecs = await detectSupportedCodecs();
            const bestCodecString = getDefaultCodec(supportedCodecs);
            const codecCategory = bestCodecString.startsWith('av01') ? 'av1' as const : 'h264' as const;
            infoLog?.log(`Initial codec selection: ${codecCategory} (${bestCodecString})`);

            // Cache supported encoder categories for later codec negotiation
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);
            infoLog?.log(`Supported encoder categories: [${this.supportedEncoderCategories.join(', ')}]`);

            // Create recording service with streaming config (uses video-pipeline internally)
            const config: RecordingConfig = {
                mode: 'webcam',
                codec: codecCategory,
                codecString: bestCodecString,
                width: 1280,
                height: 720,
                bitrate: 2_000_000,
                framerate: 30,
                bandwidth: 10_000_000,
                latency: 0,
                jitter: 0,
                packetLoss: 0,
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
                    ? { deviceId: { exact: this.selectedCameraDeviceId } }
                    : true,
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
        const matchingCodecs = codecs.filter(c => this.supportedEncoderCategories.includes(c));
        const picked = matchingCodecs.length > 0 ? matchingCodecs[0] : null;

        if (!picked) {
            warnLog?.log(`updateSupportedDecoderCodecs: no match between server codecs [${codecs.join(', ')}] and encoder capabilities [${this.supportedEncoderCategories.join(', ')}], keeping current codec`);
            return;
        }

        infoLog?.log(`Selected encoder codec: ${picked} from supported decoders: [${codecs.join(', ')}]`);
        await this.recordingService.switchCodec(picked);
    }

    /**
     * Reconfigure encoder bitrate/resolution (called from Blazor quality subscription)
     */
    public reconfigure(width: number, height: number, bitrate: number): void {
        if (!this.recordingService) {
            warnLog?.log('reconfigure: no active recording service');
            return;
        }

        // Cap to actual camera resolution — upscaling wastes CPU for no quality gain
        const cappedWidth = this.cameraWidth > 0 ? Math.min(width, this.cameraWidth) : width;
        const cappedHeight = this.cameraHeight > 0 ? Math.min(height, this.cameraHeight) : height;

        if (cappedWidth !== width || cappedHeight !== height)
            infoLog?.log(`reconfigure: ${width}x${height} @ ${bitrate / 1_000_000}Mbps → capped to ${cappedWidth}x${cappedHeight}`);
        else
            infoLog?.log(`reconfigure: ${width}x${height} @ ${bitrate / 1_000_000}Mbps`);

        void this.recordingService.getPipeline()?.reconfigure({ bitrate, width: cappedWidth, height: cappedHeight });
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
        const renderFrame = () => {
            if (!this.isRecording || !this.canvas || !this.canvasCtx) {
                console.warn('[VideoRecorder] renderFrame: exiting loop, isRecording:', this.isRecording);
                return;
            }
            frameCount++;
            if (frameCount <= 3 || frameCount % 300 === 0)
                console.warn(`[VideoRecorder] renderFrame #${frameCount}: videoWidth=${String(video.videoWidth)}, videoHeight=${String(video.videoHeight)}`);

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
                categories.add(c.category);
            }
        }
        // Return in priority order: av1, h264
        const ordered: string[] = [];
        if (categories.has('av1')) ordered.push('av1');
        if (categories.has('h264')) ordered.push('h264');
        return ordered;
    }

    public dispose() {
        if (this.disposed)
            return;
        this.disposed = true;

        // Stop rendering
        this.stopRenderingStream();

        // Stop recording service
        if (this.recordingService) {
            void this.recordingService.stop();
            this.recordingService = null;
        }

        this.isRecording = false;
    }
}

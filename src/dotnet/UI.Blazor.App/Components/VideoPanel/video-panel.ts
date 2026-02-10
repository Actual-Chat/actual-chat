// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition, @typescript-eslint/no-deprecated, @typescript-eslint/no-floating-promises */
import { fromEvent, Subject, takeUntil, filter } from 'rxjs';
import { Log } from 'logging';
import { RecordingService, type RecordingConfig, type RecordingState } from '../../Services/Video/services/recording-service';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoPanel');

export interface VideoDevice {
    deviceId: string;
    label: string;
}

export class VideoPanel {
    private blazorRef: DotNet.DotNetObject;
    private readonly videoPanel: HTMLElement;
    private readonly canvas: HTMLCanvasElement | null = null;
    private readonly canvasCtx: CanvasRenderingContext2D | null = null;
    private readonly expandBtn: HTMLElement | null = null;
    private readonly recordBtn: HTMLElement | null = null;
    private parentElement: HTMLElement | null = null;
    private disposed$: Subject<void> = new Subject<void>();

    // Video recording service (using video-pipeline)
    private recordingService: RecordingService | null = null;
    private isRecording = false;
    private animationFrameId: number | null = null;
    private previewTrack: MediaStreamTrack | null = null;
    private selectedCameraDeviceId: string | null = null;
    private sessionToken: string | null = null;
    private chatId: string | null = null;
    private isBlurEnabled = false;

    static create(videoPanel: HTMLElement, blazorRef: DotNet.DotNetObject): VideoPanel {
        return new VideoPanel(videoPanel, blazorRef);
    }

    constructor(videoPanel: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.videoPanel = videoPanel;
        if (!this.videoPanel)
            return;

        // Get canvas element for rendering
        this.canvas = this.videoPanel.querySelector('.call-video') as HTMLCanvasElement;
        if (this.canvas) {
            this.canvasCtx = this.canvas.getContext('2d');
        }

        this.parentElement = this.videoPanel.parentElement;
        const needToShowElements = this.videoPanel.querySelectorAll('.show-with-delay');
        setTimeout(() => {
            needToShowElements.forEach(element => element.classList.add('show'));
            this.videoPanel.classList.remove('first-time-open');
        }, 1000);

        // Expand button
        this.expandBtn = this.videoPanel.querySelector('.expand-btn');
        if (this.expandBtn) {
            fromEvent(this.expandBtn, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onExpandBtnClick());
        }

        // Record button
        this.recordBtn = this.videoPanel.querySelector('.record-btn');
        if (this.recordBtn) {
            fromEvent(this.recordBtn, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onRecordBtnClick());
        }

        // Escape key handler
        fromEvent<KeyboardEvent>(document, 'keydown')
            .pipe(
                takeUntil(this.disposed$),
                filter(e => e.key === 'Escape')
            )
            .subscribe(() => this.onEscPress());
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
     * Update session context for streaming
     */
    public setSessionContext(chatId: string, sessionToken: string): void {
        this.chatId = chatId;
        this.sessionToken = sessionToken;
        infoLog?.log('Session context updated for video streaming:', { chatId });
    }

    /**
     * Set whether background blur should be enabled when recording starts
     */
    public setBlurEnabled(enabled: boolean): void {
        this.isBlurEnabled = enabled;
        infoLog?.log('Background blur enabled:', enabled);
    }

    /**
     * Toggle blur on an active recording
     */
    public async toggleBlur(enabled: boolean): Promise<void> {
        this.isBlurEnabled = enabled;
        if (this.recordingService) {
            await this.recordingService.toggleBlur(enabled);
        }
    }

    /**
     * Initialize and start video recording
     */
    public async startRecording(): Promise<void> {
        console.warn('[VideoPanel] startRecording called, isRecording:', this.isRecording, 'sessionToken:', !!this.sessionToken, 'chatId:', this.chatId);
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        if (!this.sessionToken || !this.chatId) {
            console.error('[VideoPanel] Missing session context!', { sessionToken: !!this.sessionToken, chatId: this.chatId });
            throw new Error('Missing session context for video streaming');
        }

        infoLog?.log('Starting video recording...');

        try {
            // Create recording service with streaming config (uses video-pipeline internally)
            const config: RecordingConfig = {
                mode: 'webcam',
                codec: 'h264',
                width: 1280,
                height: 720,
                bitrate: 2_000_000,
                framerate: 30,
                bandwidth: 10_000_000,
                latency: 0,
                jitter: 0,
                packetLoss: 0,
                cameraDeviceId: this.selectedCameraDeviceId || undefined,
                backgroundBlur: {
                    enabled: this.isBlurEnabled,
                },
                // Enable streaming to server for real-time viewing
                streaming: {
                    enabled: true,
                    sessionToken: this.sessionToken,
                    chatId: this.chatId,
                }
            };

            console.warn('[VideoPanel] Creating RecordingService with streaming:', config.streaming);
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

            // Start recording (this initializes the video-pipeline)
            console.warn('[VideoPanel] Calling recordingService.start()...');
            await this.recordingService.start();
            console.warn('[VideoPanel] recordingService.start() completed successfully');

            // Set preview callback so blurred frames render to the preview canvas
            this.recordingService.setPreviewCallback((frame: VideoFrame) => {
                if (this.isBlurEnabled && this.canvas && this.canvasCtx) {
                    if (this.canvas.width !== frame.displayWidth || this.canvas.height !== frame.displayHeight) {
                        if (frame.displayWidth > 0 && frame.displayHeight > 0) {
                            this.canvas.width = frame.displayWidth;
                            this.canvas.height = frame.displayHeight;
                        }
                    }
                    this.canvasCtx.drawImage(frame as any, 0, 0);
                }
            });

            // Set isRecording BEFORE starting the render loop — the render loop
            // checks this flag and exits permanently if it's false on the first frame.
            this.isRecording = true;
            this.updateRecordButtonState();

            // Start rendering preview AFTER isRecording is set
            console.warn('[VideoPanel] Starting preview rendering, canvas:', !!this.canvas, 'canvasCtx:', !!this.canvasCtx);
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
            this.updateRecordButtonState();

            // Notify Blazor
            await this.blazorRef.invokeMethodAsync('OnRecordingStopped');

            infoLog?.log('Video recording stopped');
        } catch (error) {
            errorLog?.log('Failed to stop recording:', error);
        }
    }

    /**
     * Start rendering the output stream to canvas
     */
    private startRenderingStream(stream: MediaStream): void {
        const videoTrack = stream.getVideoTracks()[0];
        console.warn('[VideoPanel] startRenderingStream: videoTrack:', !!videoTrack, 'canvas:', !!this.canvas, 'canvasCtx:', !!this.canvasCtx);
        if (!videoTrack || !this.canvas || !this.canvasCtx) {
            console.error('[VideoPanel] startRenderingStream: missing required elements, aborting');
            return;
        }

        // Create a video element to render the stream
        const video = document.createElement('video');
        video.srcObject = stream;
        video.muted = true;
        video.playsInline = true;
        video.play();

        let frameCount = 0;
        const renderFrame = () => {
            if (!this.isRecording || !this.canvas || !this.canvasCtx) {
                console.warn('[VideoPanel] renderFrame: exiting loop, isRecording:', this.isRecording);
                return;
            }
            frameCount++;
            if (frameCount <= 3 || frameCount % 300 === 0)
                console.warn('[VideoPanel] renderFrame #' + frameCount + ': videoWidth=' + video.videoWidth + ', videoHeight=' + video.videoHeight);

            // When blur is enabled, the preview callback renders blurred frames directly.
            // Only draw the raw camera feed when blur is off.
            if (!this.isBlurEnabled) {
                // Resize canvas if needed
                if (this.canvas.width !== video.videoWidth || this.canvas.height !== video.videoHeight) {
                    if (video.videoWidth > 0 && video.videoHeight > 0) {
                        this.canvas.width = video.videoWidth;
                        this.canvas.height = video.videoHeight;
                        debugLog?.log(`Canvas resized to ${video.videoWidth}x${video.videoHeight}`);
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
     * Toggle recording state
     */
    public async toggleRecording(): Promise<void> {
        if (this.isRecording) {
            await this.stopRecording();
        } else {
            await this.startRecording();
        }
    }

    /**
     * Handle recorder state changes
     */
    private onRecorderStateChange(state: RecordingState): void {
        debugLog?.log('Recorder state changed:', state);

        // Update UI based on state
        if (state.status.startsWith('error')) {
            this.videoPanel.classList.add('has-error');
        } else {
            this.videoPanel.classList.remove('has-error');
        }

        // Notify Blazor of state change
        this.blazorRef.invokeMethodAsync('OnRecorderStateChanged', JSON.stringify(state));
    }

    /**
     * Handle recorder errors
     */
    private onRecorderError(error: Error): void {
        errorLog?.log('Recorder error:', error);
        this.blazorRef.invokeMethodAsync('OnRecordingError', error.message);
    }

    /**
     * Update record button visual state
     */
    private updateRecordButtonState(): void {
        if (!this.recordBtn) return;

        if (this.isRecording) {
            this.recordBtn.classList.add('recording');
            this.recordBtn.setAttribute('aria-label', 'Stop recording');
        } else {
            this.recordBtn.classList.remove('recording');
            this.recordBtn.setAttribute('aria-label', 'Start recording');
        }
    }

    /**
     * Handle record button click
     */
    private async onRecordBtnClick(): Promise<void> {
        await this.toggleRecording();
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        // Stop rendering
        this.stopRenderingStream();

        // Stop recording service
        if (this.recordingService) {
            this.recordingService.stop().catch(() => {});
            this.recordingService = null;
        }

        this.isRecording = false;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onExpandBtnClick() {
        if (!this.videoPanel.classList.contains('expanded')) {
            this.videoPanel.classList.toggle('expanded');
            document.body.appendChild(this.videoPanel);
        } else {
            this.videoPanel.classList.toggle('expanded');
            this.parentElement?.appendChild(this.videoPanel);
        }
    }

    private onEscPress() {
        if (this.videoPanel.classList.contains('expanded')) {
            this.videoPanel.classList.remove('expanded');
            this.parentElement?.appendChild(this.videoPanel);
        }
    }

    public startClosing() {
        // Stop recording before closing
        if (this.isRecording && this.recordingService) {
            this.stopRenderingStream();
            this.recordingService.stop().catch(() => {});
        }

        this.videoPanel.classList.remove('first-time-open');
        this.videoPanel.classList.add('closing');

        const content = this.videoPanel.querySelector('.c-content')!;
        let handled = false;
        const complete = () => {
            if (handled) return;
            handled = true;
            content.removeEventListener('animationend', complete);
            this.blazorRef.invokeMethodAsync('CloseVideoPanel');
        };

        content.addEventListener('animationend', complete);
        setTimeout(complete, 500); // Safety fallback if animation doesn't fire
    }
}

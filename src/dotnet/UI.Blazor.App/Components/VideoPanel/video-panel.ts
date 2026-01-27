// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition, @typescript-eslint/no-deprecated, @typescript-eslint/no-floating-promises */
import { fromEvent, Subject, takeUntil, filter } from 'rxjs';
import { Log } from 'logging';
import { RecordingService, type RecordingConfig, type RecordingState } from '../../Services/Video/services/recording-service';
import { VideoPlayer, type RemoteVideoStream } from './video-player';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoPanel');

export interface VideoDevice {
    deviceId: string;
    label: string;
}

export class VideoPanel {
    private static activeInstance: VideoPanel | null = null;

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
    private selectedCameraDeviceId: string | null = null;
    private sessionToken: string | null = null;
    private chatId: string | null = null;

    // Remote video streams (incoming from other users)
    private remoteStreams: Map<string, RemoteVideoStream> = new Map();
    private remoteContainer: HTMLElement | null = null;

    static create(videoPanel: HTMLElement, blazorRef: DotNet.DotNetObject): VideoPanel {
        const instance = new VideoPanel(videoPanel, blazorRef);
        VideoPanel.activeInstance = instance;
        return instance;
    }

    /** Returns the active VideoPanel instance, or null if not yet created */
    static getActiveInstance(): VideoPanel | null {
        return VideoPanel.activeInstance;
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
     * Initialize and start video recording
     */
    public async startRecording(): Promise<void> {
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        if (!this.sessionToken || !this.chatId) {
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
                // Enable streaming to server for real-time viewing
                streaming: {
                    enabled: true,
                    sessionToken: this.sessionToken,
                    chatId: this.chatId,
                }
            };

            this.recordingService = new RecordingService(config);

            // Listen for state changes
            this.recordingService.addEventListener('state-change', ((event: CustomEvent<RecordingState>) => {
                this.onRecorderStateChange(event.detail);
            }) as EventListener);

            // Listen for errors
            this.recordingService.addEventListener('error', ((event: CustomEvent<Error>) => {
                this.onRecorderError(event.detail);
            }) as EventListener);

            // Start recording (this initializes the video-pipeline)
            await this.recordingService.start();

            // Get output stream and render frames to canvas
            const outputStream = this.recordingService.getOutputStream();
            if (outputStream) {
                this.startRenderingStream(outputStream);
            }

            this.isRecording = true;
            this.updateRecordButtonState();

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
        if (!videoTrack || !this.canvas || !this.canvasCtx) {
            return;
        }

        // Create a video element to render the stream
        const video = document.createElement('video');
        video.srcObject = stream;
        video.muted = true;
        video.playsInline = true;
        video.play();

        const renderFrame = () => {
            if (!this.isRecording || !this.canvas || !this.canvasCtx) {
                return;
            }

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

    // ==========================================
    // Remote Video Stream Methods
    // ==========================================

    /**
     * Start a remote video stream (called from Blazor when VideoStreamEvent.Started is received)
     */
    public async startRemoteStream(
        blazorRef: DotNet.DotNetObject,
        streamId: string,
        authorId: string,
        codec: string,
        width: number,
        height: number,
        codecSettings: string
    ): Promise<void> {
        if (this.remoteStreams.has(streamId)) {
            warnLog?.log(`Remote stream ${streamId} already exists`);
            return;
        }

        infoLog?.log(`Starting remote stream ${streamId} from author ${authorId}`);

        // Create canvas for remote stream
        const canvas = this.createRemoteCanvas(streamId, authorId);

        // Create VideoPlayer
        const player = new VideoPlayer(
            blazorRef,
            streamId,
            authorId,
            codec,
            width,
            height,
            codecSettings,
            canvas
        );

        // Store remote stream info
        this.remoteStreams.set(streamId, {
            streamId,
            authorId,
            player,
            canvas
        });

        // Start the player
        player.start();

        // Update layout
        this.updateLayout();

        debugLog?.log(`Remote stream ${streamId} started`);
    }

    /**
     * Push a video frame to a remote stream (called from Blazor)
     */
    public async pushRemoteFrame(
        streamId: string,
        frameData: Uint8Array,
        timestampMs: number,
        durationMs: number,
        isKeyFrame: boolean,
        description?: Uint8Array // Codec description (SPS/PPS for H.264) - only present on keyframes
    ): Promise<void> {
        const stream = this.remoteStreams.get(streamId);
        if (!stream) {
            warnLog?.log(`Remote stream ${streamId} not found for frame push`);
            return;
        }

        await stream.player.pushFrame(frameData, timestampMs, durationMs, isKeyFrame, description);
    }

    /**
     * Stop a remote video stream (called from Blazor when VideoStreamEvent.Ended is received)
     */
    public async stopRemoteStream(streamId: string): Promise<void> {
        const stream = this.remoteStreams.get(streamId);
        if (!stream) {
            return;
        }

        infoLog?.log(`Stopping remote stream ${streamId}`);

        // Stop the player
        await stream.player.stop();

        // Remove canvas from container
        if (stream.canvas.parentElement) {
            stream.canvas.parentElement.removeChild(stream.canvas);
        }

        // Remove from map
        this.remoteStreams.delete(streamId);

        // Update layout
        this.updateLayout();

        debugLog?.log(`Remote stream ${streamId} stopped`);
    }

    /**
     * Create a canvas element for a remote stream
     */
    private createRemoteCanvas(streamId: string, authorId: string): HTMLCanvasElement {
        const canvas = document.createElement('canvas');
        canvas.className = 'remote-video';
        canvas.dataset.streamId = streamId;
        canvas.dataset.authorId = authorId;

        // Get or create remote container
        if (!this.remoteContainer) {
            this.remoteContainer = this.videoPanel.querySelector('.remote-streams');
            if (!this.remoteContainer) {
                this.remoteContainer = document.createElement('div');
                this.remoteContainer.className = 'remote-streams';
                this.videoPanel.querySelector('.c-content')?.appendChild(this.remoteContainer);
            }
        }

        this.remoteContainer.appendChild(canvas);
        return canvas;
    }

    /**
     * Update the panel layout based on active streams
     */
    private updateLayout(): void {
        const hasRemoteStreams = this.remoteStreams.size > 0;

        if (hasRemoteStreams) {
            this.videoPanel.classList.add('has-remote-streams');
            this.videoPanel.dataset.remoteStreamCount = String(this.remoteStreams.size);
        } else {
            this.videoPanel.classList.remove('has-remote-streams');
            delete this.videoPanel.dataset.remoteStreamCount;
        }
    }

    /**
     * Stop all remote streams
     */
    private async stopAllRemoteStreams(): Promise<void> {
        const streamIds = Array.from(this.remoteStreams.keys());
        for (const streamId of streamIds) {
            await this.stopRemoteStream(streamId);
        }
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        // Stop rendering
        this.stopRenderingStream();

        // Stop all remote streams
        this.stopAllRemoteStreams().catch(() => {});

        // Stop recording service
        if (this.recordingService) {
            this.recordingService.stop().catch(() => {});
            this.recordingService = null;
        }

        this.isRecording = false;

        if (VideoPanel.activeInstance === this)
            VideoPanel.activeInstance = null;

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
        const handler = () => {
            content.removeEventListener('animationend', handler);
            this.blazorRef.invokeMethodAsync('CloseVideoPanel');
        };

        content.addEventListener('animationend', handler);
    }
}

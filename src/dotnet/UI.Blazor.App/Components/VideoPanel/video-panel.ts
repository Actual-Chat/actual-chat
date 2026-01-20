// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition, @typescript-eslint/no-deprecated, @typescript-eslint/no-floating-promises */
import { fromEvent, Subject, takeUntil, filter } from 'rxjs';
import { Log } from 'logging';
import {
    VideoRecorder,
    createVideoRecorder,
} from '../../Services/Video/video-recorder';
import type {
    VideoRecorderState,
    VideoRecorderCallbacks,
    IVideoRecorder,
} from '../../Services/Video/video-recorder-contract';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoPanel');

export class VideoPanel {
    private blazorRef: DotNet.DotNetObject;
    private readonly videoPanel: HTMLElement;
    private readonly canvas: HTMLCanvasElement | null = null;
    private readonly canvasCtx: CanvasRenderingContext2D | null = null;
    private readonly expandBtn: HTMLElement | null = null;
    private readonly recordBtn: HTMLElement | null = null;
    private parentElement: HTMLElement | null = null;
    private disposed$: Subject<void> = new Subject<void>();

    // Video recorder
    private videoRecorder: IVideoRecorder | null = null;
    private isRecording = false;

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
     * Initialize and start video recording
     */
    public async startRecording(): Promise<void> {
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        infoLog?.log('Starting video recording...');

        try {
            // Create video recorder with callbacks
            const callbacks: VideoRecorderCallbacks = {
                onFrame: (frame: VideoFrame) => this.renderFrame(frame),
                onStateChange: (state: VideoRecorderState) => this.onRecorderStateChange(state),
                onError: (error: Error) => this.onRecorderError(error),
            };

            this.videoRecorder = createVideoRecorder(callbacks);

            // Initialize with default config
            await this.videoRecorder.initialize();

            // Start recording
            await this.videoRecorder.start();

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
        if (!this.isRecording || !this.videoRecorder) {
            return;
        }

        infoLog?.log('Stopping video recording...');

        try {
            await this.videoRecorder.stop();
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
     * Render a decoded video frame to the canvas
     */
    private renderFrame(frame: VideoFrame): void {
        if (!this.canvas || !this.canvasCtx) {
            return;
        }

        // Resize canvas if needed
        if (this.canvas.width !== frame.displayWidth || this.canvas.height !== frame.displayHeight) {
            this.canvas.width = frame.displayWidth;
            this.canvas.height = frame.displayHeight;
            debugLog?.log(`Canvas resized to ${frame.displayWidth}x${frame.displayHeight}`);
        }

        // Draw frame to canvas
        this.canvasCtx.drawImage(frame, 0, 0);
    }

    /**
     * Handle recorder state changes
     */
    private onRecorderStateChange(state: VideoRecorderState): void {
        debugLog?.log('Recorder state changed:', state);

        // Update UI based on state
        if (state.error) {
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

        // Stop and dispose video recorder
        if (this.videoRecorder) {
            this.videoRecorder.dispose();
            this.videoRecorder = null;
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
        if (this.isRecording && this.videoRecorder) {
            this.videoRecorder.stop().catch(() => {});
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

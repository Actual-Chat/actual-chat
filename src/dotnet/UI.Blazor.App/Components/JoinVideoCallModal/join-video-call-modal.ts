import { getLogs } from 'logging';
import { getActiveRecorder } from '../VideoPanel/video-recorder';
import { MediaCapture } from '../../Services/Video/services/media-capture';
import { CanvasVideoRenderer } from '../../Services/Video/services/canvas-video-renderer';
import { BlurPreviewSession } from '../../Services/Video/services/blur-preview-session';

const { infoLog, errorLog } = getLogs('VideoRecorder');

export class JoinVideoCallModal {
    private blazorRef: DotNet.DotNetObject;
    private readonly videoFrame: HTMLElement;
    private readonly canvasEl: HTMLCanvasElement; // on-DOM, single display canvas
    private readonly renderer: CanvasVideoRenderer;
    private track: MediaStreamTrack | null = null;
    private selectedDeviceId: string | null = null;
    private lastTrackStoppedAt = 0;
    private attachedFromRecorder = false;

    // Blur preview state
    private blurSession: BlurPreviewSession | null = null;
    private isBlurActive = false;

    static create(container: HTMLElement, blazorRef: DotNet.DotNetObject): JoinVideoCallModal {
        return new JoinVideoCallModal(container, blazorRef);
    }

    constructor(container: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.videoFrame = container.querySelector<HTMLElement>('.video-frame')!;
        this.canvasEl = this.videoFrame.querySelector<HTMLCanvasElement>('.camera-preview')!;

        this.renderer = new CanvasVideoRenderer({
            canvas: this.canvasEl,
            rafKey: 'join-video-preview',
            onFirstFrame: () => {
                void this.blazorRef.invokeMethodAsync('OnFirstFrameRendered');
            },
        });
    }

    public async startPreview(deviceId?: string): Promise<boolean> {
        await this.stopPreview();

        // Browser needs time to release camera hardware after track.stop().
        // Wait if a track was recently stopped (within the last 2 seconds).
        const timeSinceStop = performance.now() - this.lastTrackStoppedAt;
        if (this.lastTrackStoppedAt > 0 && timeSinceStop < 2000) {
            const delay = Math.max(300 - timeSinceStop, 0);
            if (delay > 0)
                await new Promise(resolve => setTimeout(resolve, delay));
        }

        try {
            this.track = await MediaCapture.captureCameraStream({
                deviceId: deviceId,
                maxRetries: 3,
            });

            // Capture the actual device ID the browser chose (important when no
            // explicit device was requested — ensures recording uses the same camera)
            const s = this.track.getSettings();
            infoLog?.log(`startPreview track: deviceId=${s.deviceId}, ${s.width}x${s.height}`);
            if (s.deviceId) this.selectedDeviceId = s.deviceId;

            this.videoFrame.classList.add('has-video');

            // Start RAF render loop to draw raw camera frames to canvas
            this.renderer.start(this.track);

            infoLog?.log('Camera preview started');
            return true;
        } catch (error) {
            errorLog?.log('Failed to start camera preview:', error);
            return false;
        }
    }

    public async stopPreview(): Promise<void> {
        // Stop blur preview first
        await this.stopBlurPreview();

        this.renderer.stop();

        if (this.track) {
            this.track.stop();
            this.track = null;
            this.lastTrackStoppedAt = performance.now();
        }

        this.videoFrame.classList.remove('has-video');

        infoLog?.log('Camera preview stopped');
    }

    public async switchCamera(deviceId: string): Promise<boolean> {
        this.selectedDeviceId = deviceId;
        const wasBlurActive = this.isBlurActive;
        // Restart preview with new device (stopPreview cleans up blur)
        const success = await this.startPreview(deviceId);
        // Restart blur if it was active
        if (success && wasBlurActive) {
            await this.startBlurPreview();
        }
        return success;
    }

    /**
     * Get the actual device ID of the currently-previewing camera.
     * Returns the device ID resolved by getUserMedia, which may differ
     * from what was originally requested (e.g., when no device was specified).
     */
    public getActualDeviceId(): string | null {
        return this.selectedDeviceId;
    }

    /**
     * Attach to the active recorder's preview stream instead of acquiring a new one.
     * Returns true if successfully attached, false if no active recorder.
     */
    public attachFromRecorder(): boolean {
        const recorder = getActiveRecorder();
        if (!recorder) return false;

        const previewTrack = recorder.getPreviewTrack();
        if (previewTrack?.readyState !== 'live') return false;

        // Clone the track so we can stop it independently
        this.track = previewTrack.clone();
        this.attachedFromRecorder = true;

        // Pause the recorder's own preview rendering
        recorder.pausePreviewRendering();

        this.videoFrame.classList.add('has-video');

        this.renderer.start(this.track);
        infoLog?.log('Attached to active recorder preview stream');
        return true;
    }

    /**
     * Detach from the recorder's stream without stopping the recorder.
     */
    public detachFromRecorder(): void {
        if (!this.attachedFromRecorder) return;

        // Stop blur preview first
        void this.stopBlurPreview();
        this.renderer.stop();

        // Stop only our cloned track
        if (this.track) {
            this.track.stop();
            this.track = null;
        }

        this.videoFrame.classList.remove('has-video');

        // Resume the recorder's own preview rendering
        const recorder = getActiveRecorder();
        if (recorder) recorder.resumePreviewRendering();

        this.attachedFromRecorder = false;
        infoLog?.log('Detached from recorder preview stream');
    }

    /**
     * Toggle blur preview on/off.
     * When enabled, starts a segmentation worker to process camera frames
     * and renders the blurred output to the same canvas.
     */
    public async toggleBlur(enabled: boolean): Promise<void> {
        if (enabled && !this.isBlurActive) {
            await this.startBlurPreview();
        } else if (!enabled && this.isBlurActive) {
            await this.stopBlurPreview();
        }
    }

    private async startBlurPreview(): Promise<void> {
        if (!this.track || this.isBlurActive) return;

        try {
            // Pause raw-frame rendering — blur callback takes over canvas drawing
            this.renderer.paused = true;

            this.blurSession = await BlurPreviewSession.create({
                source: this.renderer.video,
                target: this.canvasEl,
            });
            this.isBlurActive = true;
        } catch (error) {
            errorLog?.log('Failed to start blur preview:', error);
            this.renderer.paused = false;
            await this.stopBlurPreview();
        }
    }

    private async stopBlurPreview(): Promise<void> {
        this.isBlurActive = false;
        this.renderer.paused = false;

        if (this.blurSession) {
            await this.blurSession.stop();
            this.blurSession = null;
        }
    }

    public dispose(): void {
        if (this.attachedFromRecorder) {
            this.detachFromRecorder();
        } else {
            void this.stopBlurPreview();
            void this.stopPreview();
        }
    }
}

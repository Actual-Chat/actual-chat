import { getLogs } from 'logging';
import { MediaCapture } from '../../Services/Video/services/media-capture';
import { CanvasVideoRenderer } from '../../Services/Video/services/canvas-video-renderer';
import { BlurPreviewSession } from '../../Services/Video/services/blur-preview-session';
import { RecorderPreviewView } from '../../Services/Video/services/recorder-preview-view';

const { infoLog, warnLog, errorLog } = getLogs('JoinVideoCallModal');

export class JoinVideoCallModal {
    private blazorRef: DotNet.DotNetObject;
    private readonly videoFrame: HTMLElement;
    private readonly canvasEl: HTMLCanvasElement; // on-DOM, single display canvas
    private readonly renderer: CanvasVideoRenderer;
    private track: MediaStreamTrack | null = null;
    private selectedDeviceId: string | null = null;
    private lastTrackStoppedAt = 0;

    // Settings-mode preview: follows the active recorder via the shared view so
    // the modal and VideoPanel's self-preview both render the same pipeline
    // without coordinating directly with each other.
    private recorderView: RecorderPreviewView | null = null;

    // Blur preview state (Join mode only — Settings mode gets blurred frames
    // directly from the recorder's pipeline via RecorderPreviewView).
    private blurSession: BlurPreviewSession | null = null;
    private isBlurActive = false;
    private disposed = false;

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

    // ---------- Join mode: own stream ---------------------------------------

    public async startPreview(deviceId?: string): Promise<boolean> {
        infoLog?.log(`startPreview: deviceId=${deviceId ?? '(default)'}`);
        await this.stopPreview();

        // Browser needs time to release camera hardware after track.stop().
        // Wait if a track was recently stopped (within the last 2 seconds).
        const timeSinceStop = performance.now() - this.lastTrackStoppedAt;
        if (this.lastTrackStoppedAt > 0 && timeSinceStop < 2000) {
            const delay = Math.max(300 - timeSinceStop, 0);
            if (delay > 0)
                await new Promise(resolve => setTimeout(resolve, delay));
        }

        if (this.disposed)
            return false;

        try {
            const track = await MediaCapture.captureCameraStream({
                deviceId: deviceId,
                maxRetries: 3,
            });
            // If the modal was closed while getUserMedia was in flight, stop the
            // freshly-acquired track immediately — otherwise it holds the camera
            // hardware and the next getUserMedia fails with NotReadableError.
            // (TS narrows `this.disposed` to false after the pre-await check,
            //  but dispose() can flip it during the await.)
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (this.disposed) {
                warnLog?.log('startPreview: disposed during getUserMedia — stopping track');
                track.stop();
                this.lastTrackStoppedAt = performance.now();
                return false;
            }
            this.track = track;

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
        infoLog?.log(`switchCamera: deviceId=${deviceId}`);
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

    // Returns deviceId + facingMode for the modal's own preview track (Join mode).
    // Consumed by Blazor to resolve per-camera display preferences (mirror).
    public getCurrentCameraInfo(): { deviceId: string | null; facingMode: string | null } {
        const s = this.track?.getSettings();
        return {
            deviceId: s?.deviceId ?? this.selectedDeviceId,
            facingMode: s?.facingMode ?? null,
        };
    }

    // ---------- Settings mode: follow the active recorder -------------------

    /**
     * Attach the modal's canvas to the currently-active recorder via the shared
     * RecorderPreviewView. Re-attachment on camera switch is automatic — the
     * view watches the recorder and swaps tracks as they change.
     */
    public attachToRecorder(): void {
        if (this.recorderView) return;
        this.recorderView = RecorderPreviewView.create({
            canvas: this.canvasEl,
            rafKey: 'join-video-preview',
            onDetach: () => this.videoFrame.classList.remove('has-video'),
            onFirstFrame: () => {
                this.videoFrame.classList.add('has-video');
                void this.blazorRef.invokeMethodAsync('OnFirstFrameRendered');
            },
        });
    }

    // ---------- Blur (Join mode only) ---------------------------------------

    /**
     * Toggle blur preview on/off in Join (own-stream) mode. In Settings mode
     * blur is controlled by the recorder; the view picks up blurred frames from
     * its pipeline automatically — no local segmentation worker needed.
     */
    public async toggleBlur(enabled: boolean): Promise<void> {
        if (this.recorderView) return; // Settings mode — no-op.
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

    // ---------- Teardown ----------------------------------------------------

    public dispose(): void {
        infoLog?.log(`dispose: trackState=${this.track?.readyState ?? '(null)'}`);
        this.disposed = true;
        if (this.recorderView) {
            this.recorderView.dispose();
            this.recorderView = null;
            return;
        }
        void this.stopBlurPreview();
        void this.stopPreview();
    }
}

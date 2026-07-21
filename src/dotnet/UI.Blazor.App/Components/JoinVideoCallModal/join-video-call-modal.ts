import { getLogs } from 'logging';
import { MediaCapture } from '../../Services/Video/services/media-capture';
import { RecorderPreviewView } from '../../Services/Video/services/recorder-preview-view';
import { renderJpegFrame } from '../../Services/Video/services/jpeg-frame-renderer';

const { infoLog, warnLog, errorLog } = getLogs('JoinVideoCallModal');

export class JoinVideoCallModal {
    private blazorRef: DotNet.DotNetObject;
    private readonly videoFrame: HTMLElement;
    private readonly canvasEl: HTMLCanvasElement;
    private readonly videoEl: HTMLVideoElement;
    private track: MediaStreamTrack | null = null;
    private selectedDeviceId: string | null = null;
    private lastTrackStoppedAt = 0;
    private firstFrameFired = false;

    // Settings-mode preview: follows the active recorder via the shared view so
    // the modal and VideoPanel's self-preview both render the same pipeline
    // without coordinating directly with each other.
    private recorderView: RecorderPreviewView | null = null;

    // Blur preview was wired through the legacy `videoProcessingWorker`'s
    // preview-only mode, which the new pipeline doesn't support. The Join
    // modal now shows the raw camera track in both blurred and unblurred
    // states; blur kicks in once recording actually starts.
    // TODO Phase 7.x: re-introduce a preview-only path in the new pipeline.
    private isBlurActive = false;
    private disposed = false;

    static create(container: HTMLElement, blazorRef: DotNet.DotNetObject): JoinVideoCallModal {
        return new JoinVideoCallModal(container, blazorRef);
    }

    constructor(container: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.videoFrame = container.querySelector<HTMLElement>('.video-frame')!;
        this.canvasEl = this.videoFrame.querySelector<HTMLCanvasElement>('.camera-preview')!;
        this.videoEl = this.videoFrame.querySelector<HTMLVideoElement>('.camera-preview-video')!;

        // Native first-frame signal — fires on iOS Safari unlike rVFC for hidden videos.
        this.videoEl.addEventListener('loadeddata', this.onVideoLoadedData);
    }

    private readonly onVideoLoadedData = (): void => {
        if (this.firstFrameFired) return;
        this.firstFrameFired = true;
        void this.blazorRef.invokeMethodAsync('OnFirstFrameRendered');
    };

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

            this.firstFrameFired = false;
            this.videoEl.srcObject = new MediaStream([this.track]);
            // .catch swallows AbortError when srcObject is cleared mid-play (rapid stop).
            this.videoEl.play().catch(() => { /* benign */ });
            this.videoFrame.classList.add('has-video', 'shows-video');

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

        this.videoEl.srcObject = null;
        this.firstFrameFired = false;

        if (this.track) {
            this.track.stop();
            this.track = null;
            this.lastTrackStoppedAt = performance.now();
        }

        this.videoFrame.classList.remove('has-video', 'shows-video', 'shows-canvas');

        infoLog?.log('Camera preview stopped');
    }

    // WKWebView getUserMedia delivers no camera frames on Mac Catalyst, so the
    // native side captures the camera and pushes downscaled JPEG frames here to
    // be drawn on the canvas.
    public async renderPreviewFrame(base64Jpeg: string): Promise<void> {
        if (this.disposed)
            return;

        try {
            if (!await renderJpegFrame(this.canvasEl, base64Jpeg, () => this.disposed))
                return;

            this.videoFrame.classList.add('has-video', 'shows-canvas');
            if (!this.firstFrameFired) {
                this.firstFrameFired = true;
                void this.blazorRef.invokeMethodAsync('OnFirstFrameRendered');
            }
        } catch (error) {
            warnLog?.log('renderPreviewFrame failed:', error);
        }
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
            videoEl: this.videoEl,
            onDetach: () => this.videoFrame.classList.remove('has-video', 'shows-video', 'shows-canvas'),
            onFirstFrame: () => {
                this.videoFrame.classList.add('has-video', 'shows-video');
                void this.blazorRef.invokeMethodAsync('OnFirstFrameRendered');
            },
            // Native-video mode: canvas overlay shown only while blur is on.
            onBlurChange: (active) => this.videoFrame.classList.toggle('shows-canvas', active),
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

    private startBlurPreview(): Promise<void> {
        // Preview-only blur is unsupported in the new pipeline. We
        // mark the toggle as active for UI bookkeeping but the actual
        // blur kicks in once recording starts.
        this.isBlurActive = true;
        infoLog?.log('startBlurPreview: preview-only blur unsupported; raw track stays visible.');
        return Promise.resolve();
    }

    private stopBlurPreview(): Promise<void> {
        if (!this.isBlurActive) return Promise.resolve();
        this.isBlurActive = false;

        // Restore the raw camera track on <video> so the user keeps seeing
        // themselves once blur is off.
        if (this.track) {
            this.videoEl.srcObject = new MediaStream([this.track]);
            // .catch swallows AbortError when srcObject is cleared mid-play (dispose race).
            this.videoEl.play().catch(() => { /* benign */ });
        }
        return Promise.resolve();
    }

    // ---------- Teardown ----------------------------------------------------

    public dispose(): void {
        infoLog?.log(`dispose: trackState=${this.track?.readyState ?? '(null)'}`);
        this.disposed = true;
        this.videoEl.removeEventListener('loadeddata', this.onVideoLoadedData);
        if (this.recorderView) {
            this.recorderView.dispose();
            this.recorderView = null;
            return;
        }
        void this.stopBlurPreview();
        void this.stopPreview();
    }
}

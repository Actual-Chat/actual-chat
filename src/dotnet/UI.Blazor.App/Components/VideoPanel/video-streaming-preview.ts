import { getLogs } from 'logging';
import { getActiveRecorder, type VideoRecorder } from './video-recorder';

const { infoLog } = getLogs('VideoStreamingPreview');

export class VideoStreamingPreview {
    private static readonly BG_CANVAS_WIDTH = 64;

    private readonly element: HTMLElement;
    private readonly canvas: HTMLCanvasElement;
    private readonly canvasCtx: CanvasRenderingContext2D;
    private readonly bgCanvas: HTMLCanvasElement | null;
    private readonly bgCanvasCtx: CanvasRenderingContext2D | null;
    private animationFrameId: number | null = null;
    private attachedRecorder: VideoRecorder | null = null;
    private video: HTMLVideoElement | null = null;
    private disposed = false;

    static create(element: HTMLElement): VideoStreamingPreview {
        return new VideoStreamingPreview(element);
    }

    constructor(element: HTMLElement) {
        this.element = element;
        this.canvas = this.element.querySelector('.call-video')!;
        this.canvasCtx = this.canvas.getContext('2d')!;
        this.bgCanvas = this.element.querySelector('.remote-video-bg');
        this.bgCanvasCtx = this.bgCanvas?.getContext('2d') ?? null;

        // Start the render loop — it will auto-attach/detach to the active recorder
        this.animationFrameId = requestAnimationFrame(() => this.renderLoop());
    }

    private renderLoop(): void {
        if (this.disposed)
            return;

        const recorder = getActiveRecorder();

        if (recorder && recorder !== this.attachedRecorder) {
            // New recorder appeared — attach
            this.attach(recorder);
        } else if (!recorder && this.attachedRecorder) {
            // Recorder gone — detach
            this.detach();
        }

        // Render frame if attached
        if (this.attachedRecorder && this.video) {
            if (!this.attachedRecorder.isPreviewPaused()) {
                // When blur is active, the onPreviewFrame callback handles rendering.
                // Only draw the raw camera feed when blur is off.
                if (!this.attachedRecorder.isBlurActive()) {
                    this.renderVideoFrame();
                }
            }
        }

        this.animationFrameId = requestAnimationFrame(() => this.renderLoop());
    }

    private renderVideoFrame(): void {
        if (!this.video)
            return;

        const { videoWidth, videoHeight } = this.video;
        if (videoWidth === 0 || videoHeight === 0)
            return;

        // Mark first frame received — hides loading spinner
        if (!this.element.classList.contains('has-video'))
            this.element.classList.add('has-video');

        // Resize canvas if needed
        if (this.canvas.width !== videoWidth || this.canvas.height !== videoHeight) {
            this.canvas.width = videoWidth;
            this.canvas.height = videoHeight;
        }

        this.canvasCtx.drawImage(this.video, 0, 0);
        this.drawBgFrame(this.video, videoWidth, videoHeight);
    }

    // Draws a low-resolution copy of the frame into the background canvas.
    // The bg canvas is shown (scaled + blurred) only when the container is focused.
    private drawBgFrame(source: CanvasImageSource, width: number, height: number): void {
        if (!this.bgCanvas || !this.bgCanvasCtx)
            return;
        const bgW = VideoStreamingPreview.BG_CANVAS_WIDTH;
        const bgH = Math.max(1, Math.round(bgW * height / Math.max(1, width)));
        if (this.bgCanvas.width !== bgW || this.bgCanvas.height !== bgH) {
            this.bgCanvas.width = bgW;
            this.bgCanvas.height = bgH;
        }
        this.bgCanvasCtx.drawImage(source, 0, 0, bgW, bgH);
    }

    private attach(recorder: VideoRecorder): void {
        this.detach();
        this.attachedRecorder = recorder;

        const track = recorder.getPreviewTrack();
        if (track?.readyState !== 'live') {
            infoLog?.log('Recorder has no live preview track yet');
            this.attachedRecorder = null;
            return;
        }

        infoLog?.log('Attached to active recorder');

        // Create a video element to render the track
        this.video = document.createElement('video');
        this.video.srcObject = new MediaStream([track]);
        this.video.muted = true;
        this.video.playsInline = true;
        void this.video.play();

        // Register for blur preview frames
        recorder.onPreviewFrame = (frame: VideoFrame) => {
            if (this.canvas.width !== frame.displayWidth || this.canvas.height !== frame.displayHeight) {
                if (frame.displayWidth > 0 && frame.displayHeight > 0) {
                    this.canvas.width = frame.displayWidth;
                    this.canvas.height = frame.displayHeight;
                }
            }
            if (!this.element.classList.contains('has-video'))
                this.element.classList.add('has-video');
            this.canvasCtx.drawImage(frame, 0, 0);
            this.drawBgFrame(frame, frame.displayWidth, frame.displayHeight);
        };

        // Apply screencast class based on recorder mode
        if (recorder.isScreencastActive())
            this.element.classList.add('screencast');
        else
            this.element.classList.remove('screencast');
    }

    private detach(): void {
        if (!this.attachedRecorder)
            return;

        infoLog?.log('Detached from recorder');

        // Unregister blur callback
        if (this.attachedRecorder.onPreviewFrame)
            this.attachedRecorder.onPreviewFrame = null;

        this.attachedRecorder = null;

        // Clean up video element
        if (this.video) {
            this.video.srcObject = null;
            this.video = null;
        }

        this.element.classList.remove('has-video');
        this.element.classList.remove('screencast');
    }

    public dispose(): void {
        if (this.disposed)
            return;
        this.disposed = true;

        if (this.animationFrameId !== null) {
            cancelAnimationFrame(this.animationFrameId);
            this.animationFrameId = null;
        }

        this.detach();
    }
}

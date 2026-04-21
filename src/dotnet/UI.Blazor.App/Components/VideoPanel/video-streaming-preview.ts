import { getLogs } from 'logging';
import { getActiveRecorder, type VideoRecorder } from './video-recorder';
import { CanvasVideoRenderer } from '../../Services/Video/services/canvas-video-renderer';
import { CanvasTarget } from '../../Services/Video/services/canvas-target';

const { infoLog } = getLogs('VideoStreamingPreview');

export class VideoStreamingPreview {
    private static readonly BG_CANVAS_WIDTH = 64;

    private readonly element: HTMLElement;
    private readonly canvas: CanvasTarget;
    private readonly bgCanvas: CanvasTarget | null;
    private animationFrameId: number | null = null;
    private attachedRecorder: VideoRecorder | null = null;
    private renderer: CanvasVideoRenderer | null = null;
    private disposed = false;

    static create(element: HTMLElement): VideoStreamingPreview {
        return new VideoStreamingPreview(element);
    }

    constructor(element: HTMLElement) {
        this.element = element;
        this.canvas = new CanvasTarget(this.element.querySelector('.call-video')!);
        this.bgCanvas = new CanvasTarget(this.element.querySelector('.remote-video-bg')!);
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

        // Sync pause/blur state from recorder to renderer
        if (this.attachedRecorder && this.renderer) {
            this.renderer.paused = this.attachedRecorder.isPreviewPaused()
                || this.attachedRecorder.isBlurActive();
        }

        this.animationFrameId = requestAnimationFrame(() => this.renderLoop());
    }

    // Draws a low-resolution copy of the frame into the background canvas.
    // The bg canvas is shown (scaled + blurred) only when the container is focused.
    private drawBgFrame(source: CanvasImageSource, width: number, height: number): void {
        if (!this.bgCanvas) return;
        const bgW = VideoStreamingPreview.BG_CANVAS_WIDTH;
        const bgH = Math.max(1, Math.round(bgW * height / Math.max(1, width)));
        this.bgCanvas.draw(source, bgW, bgH);
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

        this.renderer = new CanvasVideoRenderer({
            canvas: this.canvas.element,
            rafKey: 'video-streaming-preview',
            onFirstFrame: () => {
                this.markHasVideo();
            },
            onAfterDraw: (video) => {
                this.drawBgFrame(video, video.videoWidth, video.videoHeight);
            },
        });
        this.renderer.start(track);

        // Register for blur preview frames
        recorder.onPreviewFrame = (frame: VideoFrame) => {
            this.canvas.draw(frame, frame.displayWidth, frame.displayHeight);
            this.markHasVideo();
            this.drawBgFrame(frame, frame.displayWidth, frame.displayHeight);
        };

        // Apply screencast class based on recorder mode
        if (recorder.isScreencastActive())
            this.element.classList.add('screencast');
        else
            this.element.classList.remove('screencast');
    }

    private markHasVideo(): void {
        if (!this.element.classList.contains('has-video'))
            this.element.classList.add('has-video');
    }

    private detach(): void {
        if (!this.attachedRecorder)
            return;

        infoLog?.log('Detached from recorder');

        // Unregister blur callback
        if (this.attachedRecorder.onPreviewFrame)
            this.attachedRecorder.onPreviewFrame = null;

        this.attachedRecorder = null;

        // Clean up renderer
        if (this.renderer) {
            this.renderer.dispose();
            this.renderer = null;
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

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
    // The preview track captured on the last attach. Compared to the recorder's
    // current track each frame so we re-attach when the recorder restarts its
    // pipeline (e.g. on a successful camera switch) and detach when it clears
    // the track (e.g. on a failed switch).
    private attachedTrack: MediaStreamTrack | null = null;
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
        const track = recorder?.getPreviewTrack() ?? null;
        const trackLive = track?.readyState === 'live';

        if (recorder && trackLive && track !== this.attachedTrack) {
            // New live track — (re-)attach to start rendering it.
            this.attach(recorder, track);
        } else if (this.attachedRecorder && (!recorder || !trackLive)) {
            // Recorder gone or its track became invalid — detach.
            this.detach();
        }

        // Sync pause/blur state from recorder to renderer
        if (this.attachedRecorder && this.renderer) {
            this.renderer.paused = this.attachedRecorder.isPreviewPaused()
                || this.attachedRecorder.isBlurActive();
        }

        // `.starting` drives the loading spinner: visible whenever we have a recorder that
        // still intends to produce video (not in the interrupted state) and we haven't yet
        // rendered the first frame (no `.has-video`). Blazor clears the error message before
        // any switch so the spinner doesn't coexist with the red error overlay.
        const wantsStarting = recorder != null
            && !recorder.isRecordingInterrupted()
            && !this.element.classList.contains('has-video');
        this.element.classList.toggle('starting', wantsStarting);

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

    private attach(recorder: VideoRecorder, track: MediaStreamTrack): void {
        this.detach();
        this.attachedRecorder = recorder;
        this.attachedTrack = track;

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
        // Drop the starting state immediately so the spinner doesn't flicker
        // for one frame before the renderLoop re-evaluates.
        this.element.classList.remove('starting');
    }

    private detach(): void {
        if (!this.attachedRecorder)
            return;

        infoLog?.log('Detached from recorder');

        // Unregister blur callback
        if (this.attachedRecorder.onPreviewFrame)
            this.attachedRecorder.onPreviewFrame = null;

        this.attachedRecorder = null;
        this.attachedTrack = null;

        // Clean up renderer
        if (this.renderer) {
            this.renderer.dispose();
            this.renderer = null;
        }

        // Wipe stale frames so a failed re-attach doesn't leave the last good frame on-screen
        this.canvas.clear();
        this.bgCanvas?.clear();

        this.element.classList.remove('has-video');
        this.element.classList.remove('screencast');
        // `.starting` is re-evaluated by renderLoop; no need to clear it here.
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

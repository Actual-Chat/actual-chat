import { getLogs } from 'logging';
import { getActiveRecorder, type VideoRecorder } from './video-recorder';

const { debugLog, infoLog, warnLog } = getLogs('VideoStreamingPreview');

export class VideoStreamingPreview {
    private static readonly BG_CANVAS_WIDTH = 64;

    private readonly element: HTMLElement;
    private readonly canvas: HTMLCanvasElement;
    private readonly canvasCtx: CanvasRenderingContext2D;
    private readonly bgCanvas: HTMLCanvasElement | null;
    private readonly bgCanvasCtx: CanvasRenderingContext2D | null;
    // Single reusable off-DOM <video> element. Swap srcObject on camera change
    // instead of rebuilding — a fresh element after a track swap sometimes
    // doesn't deliver frames in Chrome (black canvas).
    private readonly videoEl: HTMLVideoElement;
    private animationFrameId: number | null = null;
    private attachedRecorder: VideoRecorder | null = null;
    // The track we're currently rendering. Kept alongside attachedRecorder so
    // we can detect when the recorder swaps its track underneath us (camera
    // switch / facing flip) — the recorder identity stays the same but the
    // MediaStreamTrack reference changes.
    private attachedTrack: MediaStreamTrack | null = null;
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

        this.videoEl = document.createElement('video');
        this.videoEl.muted = true;
        this.videoEl.playsInline = true;
        this.videoEl.autoplay = true;

        // Start the render loop — it will auto-attach/detach to the active recorder
        this.animationFrameId = requestAnimationFrame(() => this.renderLoop());
    }

    private renderLoop(): void {
        if (this.disposed)
            return;

        const recorder = getActiveRecorder();
        const recorderTrack = recorder?.getPreviewTrack() ?? null;

        if (recorder && (recorder !== this.attachedRecorder || recorderTrack !== this.attachedTrack)) {
            // New recorder or recorder swapped its track (camera switch / facing flip)
            this.attach(recorder);
        } else if (!recorder && this.attachedRecorder) {
            this.detach();
        }

        // Render frame if attached
        if (this.attachedRecorder && this.attachedTrack) {
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
        const { videoWidth, videoHeight } = this.videoEl;
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

        this.canvasCtx.drawImage(this.videoEl, 0, 0);
        this.drawBgFrame(this.videoEl, videoWidth, videoHeight);
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
            debugLog?.log(`attach: no live track yet (readyState=${track?.readyState ?? 'null'})`);
            this.attachedRecorder = null;
            return;
        }
        this.attachedTrack = track;

        const s = track.getSettings();
        infoLog?.log(`attach: deviceId=${s.deviceId}, ${s.width}x${s.height}, facingMode=${s.facingMode ?? '(none)'}`);

        this.videoEl.srcObject = new MediaStream([track]);
        this.videoEl.play().catch(err => warnLog?.log('videoEl.play() rejected:', err));

        // If the track ends (camera unplugged, source reset), force a re-attach
        // on the next render tick rather than staring at dead frames.
        track.addEventListener('ended', () => {
            if (this.attachedTrack === track) {
                infoLog?.log('attached track ended — detaching');
                this.detach();
            }
        });

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

        infoLog?.log('detach');

        // Unregister blur callback
        if (this.attachedRecorder.onPreviewFrame)
            this.attachedRecorder.onPreviewFrame = null;

        this.attachedRecorder = null;
        this.attachedTrack = null;

        // Release the track but keep the <video> element — next attach just
        // swaps srcObject. Recreating the element after a camera switch can
        // leave frames from the new track undelivered on some browsers.
        this.videoEl.srcObject = null;

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

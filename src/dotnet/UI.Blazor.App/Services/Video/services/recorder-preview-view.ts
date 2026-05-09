// Follows the currently active VideoRecorder and renders its preview to a
// caller-supplied <video srcObject> (raw track) plus an optional <canvas>
// overlay (blurred frames). Multiple UI surfaces can attach independently.
// Caller-specific UI state (.starting, .has-video) is signalled via the
// onAttach/onDetach/onFirstFrame hooks — this class doesn't touch CSS.

import { getLogs } from 'logging';
import {
    addActiveRecorderListener,
    getActiveRecorder,
    type PreviewFrameListener,
    type VideoRecorder,
} from '../../../Components/VideoPanel/video-recorder';
import { CanvasTarget } from './canvas-target';
import { BG_CANVAS_WIDTH, BG_DRAW_INTERVAL_MS, BG_FILTER } from './bg-canvas-settings';

const { infoLog } = getLogs('VideoRecorder');

export interface RecorderPreviewViewOptions {
    // Blur overlay only — raw track always goes to videoEl.
    canvas: HTMLCanvasElement;
    videoEl: HTMLVideoElement;
    bgCanvas?: HTMLCanvasElement;
    // Recorder kinds to follow in priority order; defaults to [0] (camera).
    sourceKinds?: number[];
    onAttach?: (recorder: VideoRecorder) => void;
    onDetach?: () => void;
    onFirstFrame?: () => void;
    onStartingChange?: (starting: boolean) => void;
    onBlurChange?: (blurActive: boolean) => void;
}

export class RecorderPreviewView {
    private readonly options: RecorderPreviewViewOptions;
    private readonly canvasTarget: CanvasTarget;
    private readonly bgCanvasTarget: CanvasTarget | null;
    private readonly bgContainer: HTMLElement | null;
    private readonly sourceKinds: number[];
    private lastBgDrawTime = 0;

    private attachedRecorder: VideoRecorder | null = null;
    private attachedTrack: MediaStreamTrack | null = null;
    private unsubscribeFrames: (() => void) | null = null;
    private firstFrameFired = false;
    private lastStarting = false;
    private lastBlurActive = false;
    private disposed = false;
    private _paused = false;
    private videoLoadedDataListener: (() => void) | null = null;
    private bgPumpHandle: number | null = null;
    private unsubscribeRegistry: (() => void) | null = null;
    // May differ from attachedRecorder until the followed recorder's track goes live.
    private followedRecorder: VideoRecorder | null = null;
    private followUnsubscribers: (() => void)[] = [];

    static create(options: RecorderPreviewViewOptions): RecorderPreviewView {
        return new RecorderPreviewView(options);
    }

    constructor(options: RecorderPreviewViewOptions) {
        this.options = options;
        this.canvasTarget = new CanvasTarget(options.canvas);
        this.bgCanvasTarget = options.bgCanvas ? new CanvasTarget(options.bgCanvas, false, BG_FILTER) : null;
        this.bgContainer = options.bgCanvas?.parentElement ?? null;
        this.sourceKinds = options.sourceKinds ?? [0];

        // Reconcile immediately — recorder may already be registered before this view exists.
        this.unsubscribeRegistry = addActiveRecorderListener((_recorder, kind) => {
            if (!this.sourceKinds.includes(kind)) return;
            this.followRecorder(this.pickRecorder());
        });
        this.followRecorder(this.pickRecorder());
    }

    private pickRecorder(): VideoRecorder | null {
        for (const k of this.sourceKinds) {
            const r = getActiveRecorder(k);
            if (r) return r;
        }
        return null;
    }

    public isAttached(): boolean {
        return this.attachedRecorder !== null;
    }

    // While paused, drawing and attach/detach are frozen so the last frame stays
    // on the canvas (independent of the recorder's own pause flag).
    public get paused(): boolean {
        return this._paused;
    }

    public set paused(value: boolean) {
        if (this._paused === value) return;
        this._paused = value;
        this.syncAttachment();
    }

    public dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        if (this.unsubscribeRegistry) {
            this.unsubscribeRegistry();
            this.unsubscribeRegistry = null;
        }
        for (const u of this.followUnsubscribers) u();
        this.followUnsubscribers = [];
        this.followedRecorder = null;
        this.detach();
    }

    private followRecorder(recorder: VideoRecorder | null): void {
        if (this.followedRecorder === recorder) {
            this.syncAttachment();
            return;
        }
        for (const u of this.followUnsubscribers) u();
        this.followUnsubscribers = [];
        this.followedRecorder = recorder;
        if (recorder) {
            const resync = () => this.syncAttachment();
            this.followUnsubscribers = [
                recorder.addStateChangeListener(resync),
                recorder.addBlurChangeListener(resync),
            ];
        }
        this.syncAttachment();
    }

    private syncAttachment(): void {
        if (this.disposed) return;
        const recorder = this.followedRecorder;
        const paused = this._paused;

        // While paused, skip attach/detach: detach() would clear the canvas and
        // flash the spinner even though another consumer is driving rendering.
        if (!paused) {
            const track = recorder?.getPreviewTrack() ?? null;
            const trackLive = track?.readyState === 'live';
            if (recorder && trackLive && track !== this.attachedTrack) {
                this.attach(recorder, track);
            } else if (this.attachedRecorder && (!recorder || !trackLive)) {
                this.detach();
            }
        }

        if (this.attachedRecorder) {
            if (paused) this.options.videoEl.pause(); else void this.options.videoEl.play();
        }

        const isStarting = !paused
            && recorder?.recordingState === 'starting'
            && !this.firstFrameFired;
        if (isStarting !== this.lastStarting) {
            this.lastStarting = isStarting;
            this.options.onStartingChange?.(isStarting);
        }

        const blurActive = recorder?.isBlurActive() ?? false;
        if (blurActive !== this.lastBlurActive) {
            this.lastBlurActive = blurActive;
            this.options.onBlurChange?.(blurActive);
        }
    }

    private attach(recorder: VideoRecorder, track: MediaStreamTrack): void {
        this.detach();
        this.attachedRecorder = recorder;
        this.attachedTrack = track;
        this.firstFrameFired = false;

        infoLog?.log('Attached to active recorder');

        const videoEl = this.options.videoEl;
        videoEl.srcObject = new MediaStream([track]);
        void videoEl.play();
        this.videoLoadedDataListener = () => this.fireFirstFrame();
        videoEl.addEventListener('loadeddata', this.videoLoadedDataListener);
        if (this.bgCanvasTarget) {
            this.bgPumpHandle = window.setInterval(() => {
                if (this._paused) return;
                // When blur is on, the blur listener already paints the bg from the
                // blurred canvas — sourcing from raw video here would overwrite it.
                if (this.attachedRecorder?.isBlurActive()) return;
                if (videoEl.videoWidth === 0 || videoEl.videoHeight === 0) return;
                this.drawBgFrameFromVideo(videoEl);
            }, BG_DRAW_INTERVAL_MS);
        }

        const blurListener: PreviewFrameListener = (frame: VideoFrame) => {
            if (this._paused)
                return;
            this.canvasTarget.draw(frame, frame.displayWidth, frame.displayHeight);
            this.drawBgFrame(frame.displayWidth, frame.displayHeight);
            this.fireFirstFrame();
        };
        this.unsubscribeFrames = recorder.addPreviewFrameListener(blurListener);

        this.options.onAttach?.(recorder);
    }

    private detach(): void {
        if (!this.attachedRecorder) return;

        infoLog?.log('Detached from recorder');

        if (this.unsubscribeFrames) {
            this.unsubscribeFrames();
            this.unsubscribeFrames = null;
        }
        this.attachedRecorder = null;
        this.attachedTrack = null;

        const videoEl = this.options.videoEl;
        if (this.videoLoadedDataListener) {
            videoEl.removeEventListener('loadeddata', this.videoLoadedDataListener);
            this.videoLoadedDataListener = null;
        }
        videoEl.srcObject = null;
        if (this.bgPumpHandle !== null) {
            clearInterval(this.bgPumpHandle);
            this.bgPumpHandle = null;
        }

        // Wipe stale frames so a failed re-attach doesn't leave the last good
        // frame on-screen while the new pipeline spins up.
        this.canvasTarget.clear();
        this.bgCanvasTarget?.clear();

        this.firstFrameFired = false;
        this.options.onDetach?.();
    }

    private fireFirstFrame(): void {
        if (this.firstFrameFired) return;
        this.firstFrameFired = true;
        if (this.lastStarting) {
            this.lastStarting = false;
            this.options.onStartingChange?.(false);
        }
        this.options.onFirstFrame?.();
    }

    private drawBgFrame(width: number, height: number): void {
        if (!this.bgCanvasTarget) return;
        if (!this.bgContainer?.classList.contains('item-focused')) return;
        const now = performance.now();
        if (now - this.lastBgDrawTime < BG_DRAW_INTERVAL_MS) return;
        this.lastBgDrawTime = now;

        const bgW = BG_CANVAS_WIDTH;
        const bgH = Math.max(1, Math.round(bgW * height / Math.max(1, width)));
        // Source from the already-drawn main canvas to avoid a second
        // GPU->RGB conversion of the VideoFrame per frame.
        this.bgCanvasTarget.draw(this.canvasTarget.element, bgW, bgH);
    }

    // Sourced from <video> because the main canvas is empty in the raw-track path.
    private drawBgFrameFromVideo(videoEl: HTMLVideoElement): void {
        if (!this.bgCanvasTarget) return;
        if (!this.bgContainer?.classList.contains('item-focused')) return;
        const w = videoEl.videoWidth;
        const h = videoEl.videoHeight;
        const bgW = BG_CANVAS_WIDTH;
        const bgH = Math.max(1, Math.round(bgW * h / Math.max(1, w)));
        this.bgCanvasTarget.draw(videoEl, bgW, bgH);
    }
}

/**
 * CanvasVideoRenderer
 * Manages an off-DOM HTMLVideoElement → on-DOM HTMLCanvasElement render loop.
 * Given a MediaStreamTrack, creates a hidden video element, attaches the track,
 * and draws frames to the target canvas at the video's native frame rate using
 * requestVideoFrameCallback.
 */

import { CanvasTarget } from './canvas-target';

export interface CanvasVideoRendererOptions {
    canvas: HTMLCanvasElement;
    /** Called once after the first frame is drawn. */
    onFirstFrame?: () => void;
    /** Called each frame after drawImage — receives the video element as a source. */
    onAfterDraw?: (video: HTMLVideoElement) => void;
    /** Unique per-instance key (kept for API compat, no longer used for scheduling). */
    rafKey: string;
}

export class CanvasVideoRenderer {
    private readonly target: CanvasTarget;
    private readonly videoEl: HTMLVideoElement;
    private readonly options: CanvasVideoRendererOptions;
    private running = false;
    private _paused = false;
    private firstFrameFired = false;
    private vfcHandle = 0;

    get canvas(): HTMLCanvasElement { return this.options.canvas; }
    get video(): HTMLVideoElement { return this.videoEl; }
    get isRunning(): boolean { return this.running; }

    get paused(): boolean { return this._paused; }
    set paused(value: boolean) { this._paused = value; }

    constructor(options: CanvasVideoRendererOptions) {
        this.options = options;
        this.target = new CanvasTarget(options.canvas);

        this.videoEl = document.createElement('video');
        this.videoEl.muted = true;
        this.videoEl.playsInline = true;
        this.videoEl.autoplay = true;
    }

    /** Attach a track and start the render loop. Replaces any previous track. */
    start(track: MediaStreamTrack): void {
        this.videoEl.srcObject = new MediaStream([track]);
        // Off-DOM video elements don't honor autoplay — must call play() explicitly
        void this.videoEl.play();

        this.running = true;
        this._paused = false;
        this.firstFrameFired = false;
        this.scheduleFrame();
    }

    /** Stop the render loop and detach the track. Does NOT stop the track itself. */
    stop(): void {
        this.running = false;
        this._paused = false;
        this.firstFrameFired = false;
        if (this.vfcHandle) {
            this.videoEl.cancelVideoFrameCallback(this.vfcHandle);
            this.vfcHandle = 0;
        }
        this.videoEl.srcObject = null;
    }

    dispose(): void {
        this.stop();
    }

    private scheduleFrame(): void {
        if (!this.running) return;
        this.vfcHandle = this.videoEl.requestVideoFrameCallback(this.renderFrame);
    }

    private renderFrame = (): void => {
        if (!this.running) return;

        if (!this._paused && this.videoEl.videoWidth > 0 && this.videoEl.videoHeight > 0) {
            this.target.draw(this.videoEl, this.videoEl.videoWidth, this.videoEl.videoHeight);

            if (!this.firstFrameFired) {
                this.firstFrameFired = true;
                this.options.onFirstFrame?.();
            }

            this.options.onAfterDraw?.(this.videoEl);
        }

        this.scheduleFrame();
    };
}

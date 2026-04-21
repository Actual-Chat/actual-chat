import { getActiveRecorder } from './video-recorder';
import { RecorderPreviewView } from '../../Services/Video/services/recorder-preview-view';

export class VideoStreamingPreview {
    private readonly element: HTMLElement;
    private readonly view: RecorderPreviewView;
    private spinnerRafId: number | null = null;
    private disposed = false;

    static create(element: HTMLElement): VideoStreamingPreview {
        return new VideoStreamingPreview(element);
    }

    constructor(element: HTMLElement) {
        this.element = element;
        const canvas = this.element.querySelector<HTMLCanvasElement>('.call-video')!;
        const bgCanvas = this.element.querySelector<HTMLCanvasElement>('.remote-video-bg') ?? undefined;

        this.view = RecorderPreviewView.create({
            canvas,
            bgCanvas,
            rafKey: 'video-streaming-preview',
            onAttach: (recorder) => {
                this.element.classList.toggle('screencast', recorder.isScreencastActive());
            },
            onFirstFrame: () => {
                this.element.classList.add('has-video');
                // Drop .starting immediately so the spinner doesn't flicker for
                // one frame before the next spinner-tick re-evaluates.
                this.element.classList.remove('starting');
            },
            onDetach: () => {
                this.element.classList.remove('has-video');
                this.element.classList.remove('screencast');
            },
        });

        // Drive `.starting` purely from UI state. The recorder's interrupted
        // flag can flip without any track event, so we poll each frame.
        this.spinnerRafId = requestAnimationFrame(() => this.updateStarting());
    }

    private updateStarting(): void {
        if (this.disposed) return;

        const recorder = getActiveRecorder();
        // `.starting` drives the loading spinner: visible whenever we have a
        // recorder that still intends to produce video (not interrupted) and we
        // haven't yet rendered the first frame. Blazor clears any recent error
        // before a switch so the spinner doesn't coexist with the error overlay.
        const wantsStarting = recorder != null
            && !recorder.isRecordingInterrupted()
            && !this.element.classList.contains('has-video');
        this.element.classList.toggle('starting', wantsStarting);

        this.spinnerRafId = requestAnimationFrame(() => this.updateStarting());
    }

    /**
     * Pause/resume rendering of the recorder's preview on this surface. While
     * paused the last rendered frame stays on the canvas. Used by Blazor when
     * Settings-mode JoinVideoCallModal is about to take over the preview.
     */
    public setPaused(paused: boolean): void {
        this.view.paused = paused;
    }

    public dispose(): void {
        if (this.disposed) return;
        this.disposed = true;

        if (this.spinnerRafId !== null) {
            cancelAnimationFrame(this.spinnerRafId);
            this.spinnerRafId = null;
        }

        this.view.dispose();
    }
}

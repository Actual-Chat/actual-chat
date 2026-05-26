import { RecorderPreviewView } from '../../Services/Video/services/recorder-preview-view';
import { isBgBlurOff } from '../../Services/Video/playback/bg-blur-override';

export class VideoStreamingPreview {
    private readonly element: HTMLElement;
    private readonly view: RecorderPreviewView;
    private disposed = false;

    static create(element: HTMLElement, sourceKind: number): VideoStreamingPreview {
        return new VideoStreamingPreview(element, sourceKind);
    }

    constructor(element: HTMLElement, sourceKind: number) {
        this.element = element;
        const canvas = this.element.querySelector<HTMLCanvasElement>('.call-video')!;
        // Sender's local preview shares the `?bgBlur=off` kill-switch with the
        // receiver: drop the bg canvas entirely so RecorderPreviewView never
        // constructs a BgCanvasRenderer.
        const bgCanvas = isBgBlurOff()
            ? undefined
            : this.element.querySelector<HTMLCanvasElement>('.remote-video-bg') ?? undefined;
        const videoEl = this.element.querySelector<HTMLVideoElement>('.call-video-native')!;

        this.view = RecorderPreviewView.create({
            canvas,
            videoEl,
            bgCanvas,
            sourceKinds: [sourceKind],
            onFirstFrame: () => {
                this.element.classList.add('has-video');
            },
            onDetach: () => {
                this.element.classList.remove('has-video');
            },
            onStartingChange: (starting) => {
                this.element.classList.toggle('starting', starting);
            },
        });
    }

    // Pause/resume rendering of the recorder's preview on this surface. While
    // paused the last rendered frame stays on the canvas. Used by Blazor when
    // Settings-mode JoinVideoCallModal is about to take over the preview.
    public setPaused(paused: boolean): void {
        this.view.paused = paused;
    }

    public dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.view.dispose();
    }
}

import { RecorderPreviewView } from '../../Services/Video/services/recorder-preview-view';
import { isBgBlurOff } from '../../Services/Video/playback/bg-blur-override';

function base64ToBytes(base64: string): Uint8Array {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++)
        bytes[i] = binary.charCodeAt(i);
    return bytes;
}

export class VideoStreamingPreview {
    private readonly element: HTMLElement;
    private readonly canvas: HTMLCanvasElement;
    // On Mac Catalyst the camera is captured natively and pushed here as JPEG
    // frames (getUserMedia/WebCodecs are unavailable in the WKWebView), so the
    // JS recorder never produces preview frames and RecorderPreviewView is skipped.
    private readonly view: RecorderPreviewView | null;
    private disposed = false;

    static create(element: HTMLElement, sourceKind: number, useNativePreview: boolean): VideoStreamingPreview {
        return new VideoStreamingPreview(element, sourceKind, useNativePreview);
    }

    constructor(element: HTMLElement, sourceKind: number, useNativePreview: boolean) {
        this.element = element;
        this.canvas = this.element.querySelector<HTMLCanvasElement>('.call-video')!;
        if (useNativePreview) {
            this.view = null;
            return;
        }

        // Sender's local preview shares the `?bgBlur=off` kill-switch with the
        // receiver: drop the bg canvas entirely so RecorderPreviewView never
        // constructs a BgCanvasRenderer.
        const bgCanvas = isBgBlurOff()
            ? undefined
            : this.element.querySelector<HTMLCanvasElement>('.remote-video-bg') ?? undefined;
        const videoEl = this.element.querySelector<HTMLVideoElement>('.call-video-native')!;

        this.view = RecorderPreviewView.create({
            canvas: this.canvas,
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

    // Mac Catalyst self-tile: draw a native camera JPEG frame onto the call
    // canvas. Mirroring/fit are handled by the element's CSS classes, same as
    // the RecorderPreviewView canvas path.
    public async renderPreviewFrame(base64Jpeg: string): Promise<void> {
        if (this.disposed) return;
        try {
            const bytes = base64ToBytes(base64Jpeg);
            const blob = new Blob([bytes.buffer as ArrayBuffer], { type: 'image/jpeg' });
            const bitmap = await createImageBitmap(blob);
            // dispose() can flip `disposed` during the await above.
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (this.disposed) { bitmap.close(); return; }

            const ctx = this.canvas.getContext('2d');
            if (!ctx) { bitmap.close(); return; }
            if (this.canvas.width !== bitmap.width || this.canvas.height !== bitmap.height) {
                this.canvas.width = bitmap.width;
                this.canvas.height = bitmap.height;
            }
            ctx.drawImage(bitmap, 0, 0);
            bitmap.close();
            this.element.classList.add('has-video');
        } catch {
            // A dropped preview frame is harmless.
        }
    }

    // Pause/resume rendering of the recorder's preview on this surface. While
    // paused the last rendered frame stays on the canvas. Used by Blazor when
    // Settings-mode JoinVideoCallModal is about to take over the preview.
    public setPaused(paused: boolean): void {
        if (this.view)
            this.view.paused = paused;
    }

    public dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.view?.dispose();
    }
}

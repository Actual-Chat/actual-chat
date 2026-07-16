import { renderJpegFrame } from '../../Services/Video/services/jpeg-frame-renderer';

// Mac Catalyst remote tile: the WKWebView can't decode remote H.264 (no WebCodecs
// VideoDecoder), so frames are decoded natively and pushed here as base64 JPEG. This
// draws them onto the tile's `.remote-video` canvas, bypassing the JS player/worker.
export class VideoTrackPlayerNative {
    private readonly canvas: HTMLCanvasElement;
    private readonly placeholderEl: HTMLElement | null;
    private hasFrame = false;
    private disposed = false;

    static create(canvas: HTMLCanvasElement): VideoTrackPlayerNative {
        return new VideoTrackPlayerNative(canvas);
    }

    constructor(canvas: HTMLCanvasElement) {
        this.canvas = canvas;
        this.placeholderEl = canvas.parentElement?.querySelector<HTMLElement>('.video-placeholder') ?? null;
    }

    public async renderRemoteFrame(base64Jpeg: string): Promise<void> {
        if (this.disposed)
            return;

        try {
            if (!await renderJpegFrame(this.canvas, base64Jpeg, () => this.disposed))
                return;

            if (!this.hasFrame) {
                this.hasFrame = true;
                this.placeholderEl?.classList.add('has-frame');
            }
        } catch {
            // A dropped frame is harmless.
        }
    }

    public dispose(): void {
        this.disposed = true;
    }
}

function base64ToBytes(base64: string): Uint8Array {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++)
        bytes[i] = binary.charCodeAt(i);
    return bytes;
}

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

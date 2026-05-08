import { from, type PipeOperator } from 'ix-ext';
import type { DecodedFrame } from '../frame-envelopes';

// `drawImage`-only subset of the 2D context. Tests inject a recorder.
export interface CanvasImageInterface {
    readonly canvas?: { width: number; height: number };
    drawImage(image: VideoFrame | ImageBitmap, x: number, y: number): void;
    drawImage(image: VideoFrame | ImageBitmap, x: number, y: number, w: number, h: number): void;
}

export interface CanvasPresentOptions {
    getCanvasCtx: () => CanvasImageInterface;
    /** WebKit can't `drawImage(VideoFrame)` directly — convert to a
     *  bitmap first when this is provided. Chromium/Firefox draw the
     *  `VideoFrame` directly (zero-copy). */
    convertToBitmap?: (frame: VideoFrame) => Promise<ImageBitmap>;
}

// Terminal sink: drawImage each frame stretched to the displayWidth/Height
// canvas. Resizing the backing store on every frame avoids drawing a low
// layer (e.g. 320px) into the top-left quarter of a canvas that
// was sized for the top layer (e.g. 1280px).
export function canvasPresent(opts: CanvasPresentOptions): PipeOperator<DecodedFrame, void> {
    const { getCanvasCtx, convertToBitmap } = opts;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<void> {
            let canvasCtx: CanvasImageInterface | null = null;
            for await (const decoded of source) {
                const frame = decoded.frame;
                try {
                    canvasCtx ??= getCanvasCtx();
                    const width = frame.displayWidth > 0 ? frame.displayWidth : frame.codedWidth;
                    const height = frame.displayHeight > 0 ? frame.displayHeight : frame.codedHeight;
                    if (canvasCtx.canvas && width > 0 && height > 0
                        && (canvasCtx.canvas.width !== width || canvasCtx.canvas.height !== height)) {
                        canvasCtx.canvas.width = width;
                        canvasCtx.canvas.height = height;
                    }
                    if (convertToBitmap) {
                        let bitmap: ImageBitmap | null = null;
                        try {
                            bitmap = await convertToBitmap(frame);
                            canvasCtx.drawImage(bitmap, 0, 0, width, height);
                        } finally {
                            if (bitmap) {
                                try { bitmap.close(); } catch { /* ignore */ }
                            }
                        }
                    } else {
                        canvasCtx.drawImage(frame, 0, 0, width, height);
                    }
                    decoded.stats.framesPresented++;
                } finally {
                    try { frame.close(); } catch { /* already closed */ }
                }
            }
        }
    };
}

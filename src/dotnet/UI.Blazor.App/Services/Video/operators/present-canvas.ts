import { type PipeOperator } from 'ix-ext';
import { type DecodedFrame, type PlayerStats } from '../frame-envelopes';
import { presentPacer, type PresentSink } from '../playback/present-pacer';

export interface CanvasImageInterface {
    readonly canvas?: { width: number; height: number };
    drawImage(image: VideoFrame | ImageBitmap, x: number, y: number): void;
    drawImage(image: VideoFrame | ImageBitmap, x: number, y: number, w: number, h: number): void;
}

export interface CanvasPresentOptions {
    getCanvasCtx: () => CanvasImageInterface;
    // WebKit can't drawImage(VideoFrame) directly; convert via bitmap when provided.
    convertToBitmap?: (frame: VideoFrame) => Promise<ImageBitmap>;
    getBufferSpanMs: () => number;
    targetSpanMs: number;
    nowFn?: () => number;
    delayFn?: (ms: number) => Promise<void>;
    holdMs?: number;
    getAudioCaptureOffsetMs?: () => number | null;
    stats?: PlayerStats;
}

export function canvasPresent(opts: CanvasPresentOptions): PipeOperator<DecodedFrame, void> {
    return presentPacer({
        getBufferSpanMs: opts.getBufferSpanMs,
        targetSpanMs: opts.targetSpanMs,
        nowFn: opts.nowFn,
        delayFn: opts.delayFn,
        holdMs: opts.holdMs,
        getAudioCaptureOffsetMs: opts.getAudioCaptureOffsetMs,
        createSink: (): PresentSink => {
            const canvasCtx = opts.getCanvasCtx();
            const convertToBitmap = opts.convertToBitmap;
            return {
                async present(frame: VideoFrame): Promise<boolean> {
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
                            if (opts.stats) opts.stats.presentState = 'canvas:converting';
                            bitmap = await convertToBitmap(frame);
                            if (opts.stats) opts.stats.presentState = 'canvas:drawing';
                            canvasCtx.drawImage(bitmap, 0, 0, width, height);
                        } finally {
                            if (bitmap) {
                                try { bitmap.close(); } catch { /* ignore */ }
                            }
                        }
                    } else {
                        if (opts.stats) opts.stats.presentState = 'canvas:drawing';
                        canvasCtx.drawImage(frame, 0, 0, width, height);
                    }
                    return true;
                },
            };
        },
    });
}

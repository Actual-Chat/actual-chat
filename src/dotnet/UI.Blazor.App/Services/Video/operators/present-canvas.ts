import { from, type PipeOperator } from 'ix-ext';
import { delayAsync } from 'promises';
import type { DecodedFrame } from '../frame-envelopes';

// ---- Tunables -------------------------------------------------------------
// Mirrored with `present-mstg.ts` so canvas + MSTG share the same drain
// policy. Read that file for the full pacing-rule write-up.

const MAX_FPS = 120;
const MIN_FPS = 10;
const MIN_DURATION_MS = 1000 / MAX_FPS;     // 8.33 ms — 120 fps cap
const MAX_DURATION_MS = 1000 / MIN_FPS;     // 100  ms — 10 fps floor

const CATCHUP_BUDGET_MS = 4_000;

// ---- Options --------------------------------------------------------------

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
    /** Receiver buffer's current `spanMs()`. Read fresh per frame to
     *  decide present-vs-skip; see `CATCHUP_BUDGET_MS`. */
    getBufferSpanMs: () => number;
    /** Same value passed to the buffer's `targetSpanMs`. */
    targetSpanMs: number;
    /** Test seam for `performance.now`. */
    nowFn?: () => number;
    /** Test seam for the inter-frame delay primitive. */
    delayFn?: (ms: number) => Promise<void>;
}

/**
 * Terminal sink: drawImage each frame into the canvas at a variable
 * cadence driven by the source's capture-time deltas. Same dual-mode
 * (steady / catch-up / skip) policy as `mstgPresent`. Resizes the
 * backing store on every layer-size change so a 320 px frame doesn't
 * get drawn into the top-left quarter of a canvas sized for 1280 px.
 */
export function canvasPresent(opts: CanvasPresentOptions): PipeOperator<DecodedFrame, void> {
    const { getCanvasCtx, convertToBitmap, getBufferSpanMs, targetSpanMs } = opts;
    const nowFn = opts.nowFn ?? ((): number => performance.now());
    const delayFn = opts.delayFn ?? ((ms): Promise<void> => delayAsync(ms));
    return source => from(impl(source));

    async function* impl(source: AsyncIterable<DecodedFrame>): AsyncIterable<void> {
        let canvasCtx: CanvasImageInterface | null = null;
        let lastWriteAt: number | null = null;
        let prevCapturedAt: number | null = null;
        for await (const decoded of source) {
            const frame = decoded.frame;
            try {
                const now = nowFn();
                const extraMs = Math.max(0, getBufferSpanMs() - targetSpanMs);

                if (extraMs > CATCHUP_BUDGET_MS
                    && lastWriteAt !== null
                    && now - lastWriteAt < MIN_DURATION_MS) {
                    decoded.stats.framesDroppedAtPresenter++;
                    prevCapturedAt = decoded.capturedAt.timeMs;
                    continue;
                }

                let durationMs: number;
                if (lastWriteAt === null || prevCapturedAt === null) {
                    durationMs = 0;
                } else if (extraMs > 0) {
                    durationMs = MIN_DURATION_MS;
                } else {
                    const natural = decoded.capturedAt.timeMs - prevCapturedAt;
                    durationMs = Math.max(MIN_DURATION_MS, Math.min(MAX_DURATION_MS, natural));
                }

                const baseAt: number = lastWriteAt ?? now;
                let nextWriteAt: number = baseAt + durationMs;
                if (nextWriteAt - now > MAX_DURATION_MS)
                    nextWriteAt = now + MAX_DURATION_MS;
                if (nextWriteAt > now)
                    await delayFn(nextWriteAt - now);

                canvasCtx ??= getCanvasCtx();
                const width = frame.displayWidth > 0 ? frame.displayWidth : frame.codedWidth;
                const height = frame.displayHeight > 0 ? frame.displayHeight : frame.codedHeight;
                if (canvasCtx.canvas && width > 0 && height > 0
                    && (canvasCtx.canvas.width !== width || canvasCtx.canvas.height !== height)) {
                    canvasCtx.canvas.width = width;
                    canvasCtx.canvas.height = height;
                }
                let presented = false;
                try {
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
                    presented = true;
                } finally {
                    if (!presented)
                        decoded.stats.framesDroppedAtPresenter++;
                }
                lastWriteAt = nextWriteAt;
                prevCapturedAt = decoded.capturedAt.timeMs;
            } finally {
                try { frame.close(); } catch { /* already closed */ }
            }
        }
    }
}

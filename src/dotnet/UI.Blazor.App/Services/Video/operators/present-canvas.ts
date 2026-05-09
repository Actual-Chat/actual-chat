import { from, type PipeOperator } from 'ix-ext';
import { delayAsync } from 'promises';
import type { DecodedFrame } from '../frame-envelopes';

// ---- Tunables -------------------------------------------------------------

/** 60 fps slot. Min wallclock gap between two consecutive presented frames. */
const PRESENT_PERIOD_MS = 1000 / 60;

/**
 * Buffer overflow we promise to drain via the 60 fps cap alone — see
 * `present-mstg.ts` for the rationale and full pacing rule. Mirrored
 * here so canvas playback shares the same drain policy as MSTG.
 */
const CATCHUP_BUDGET_MS = 1000;

/** Hard reset for `nextPresentMs` after a long stall — see present-mstg.ts. */
const MAX_PRESENT_DURATION_MS = 200;

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
 * Terminal sink: drawImage each frame into the canvas at a fixed 60 fps
 * cadence. Same dual-mode (in-budget / catch-up skip) policy as
 * `mstgPresent` — read that for the pacing rule. Resizes the backing
 * store on every layer-size change so a 320 px frame doesn't get drawn
 * into the top-left quarter of a canvas sized for 1280 px.
 */
export function canvasPresent(opts: CanvasPresentOptions): PipeOperator<DecodedFrame, void> {
    const { getCanvasCtx, convertToBitmap, getBufferSpanMs, targetSpanMs } = opts;
    const nowFn = opts.nowFn ?? ((): number => performance.now());
    const delayFn = opts.delayFn ?? ((ms): Promise<void> => delayAsync(ms));
    return source => from(impl(source));

    async function* impl(source: AsyncIterable<DecodedFrame>): AsyncIterable<void> {
        let canvasCtx: CanvasImageInterface | null = null;
        let lastPresentMs = Number.NEGATIVE_INFINITY;
        let nextPresentMs: number | null = null;
        for await (const decoded of source) {
            const frame = decoded.frame;
            try {
                const now = nowFn();
                const extraMs = Math.max(0, getBufferSpanMs() - targetSpanMs);
                const inBudget = extraMs <= CATCHUP_BUDGET_MS;

                if (!inBudget && now - lastPresentMs < PRESENT_PERIOD_MS) {
                    decoded.stats.framesDroppedAtPresenter++;
                    continue;
                }

                if (nextPresentMs === null) {
                    nextPresentMs = now;
                }
                else if (nextPresentMs > now) {
                    await delayFn(nextPresentMs - now);
                }
                else if (now - nextPresentMs > MAX_PRESENT_DURATION_MS) {
                    nextPresentMs = nowFn();
                }

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
                lastPresentMs = nowFn();
                nextPresentMs += PRESENT_PERIOD_MS;
            } finally {
                try { frame.close(); } catch { /* already closed */ }
            }
        }
    }
}

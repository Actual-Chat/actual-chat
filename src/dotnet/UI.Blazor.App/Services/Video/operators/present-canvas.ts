import { from, type PipeOperator } from 'ix-ext';
import { delayAsync } from 'actuallab-core';
import { aggregateDropTrace, updatePlaybackRateEma, type DecodedFrame } from '../frame-envelopes';
import { FrameDropStage } from '../frame-drop-trace';

// Pacing policy mirrored with `present-mstg.ts`.

const MAX_FPS = 120;
const MIN_FPS = 10;
const MIN_DURATION_MS = 1000 / MAX_FPS;
const MAX_DURATION_MS = 1000 / MIN_FPS;

const CATCHUP_BUDGET_MS = 4_000;

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
}

export function canvasPresent(opts: CanvasPresentOptions): PipeOperator<DecodedFrame, void> {
    const { getCanvasCtx, convertToBitmap, getBufferSpanMs, targetSpanMs } = opts;
    const nowFn = opts.nowFn ?? ((): number => performance.now());
    const delayFn = opts.delayFn ?? ((ms): Promise<void> => delayAsync(ms));
    return source => from(impl(source));

    async function* impl(source: AsyncIterable<DecodedFrame>): AsyncIterable<void> {
        let canvasCtx: CanvasImageInterface | null = null;
        let lastWriteAt: number | null = null;
        let prevCapturedAt: number | null = null;
        let prevCapturedEpoch: number | null = null;
        for await (const decoded of source) {
            const frame = decoded.frame;
            try {
                const now = nowFn();
                const extraMs = Math.max(0, getBufferSpanMs() - targetSpanMs);
                if (prevCapturedEpoch !== null && decoded.capturedAt.epoch !== prevCapturedEpoch) {
                    lastWriteAt = null;
                    prevCapturedAt = null;
                }

                if (extraMs > CATCHUP_BUDGET_MS
                    && lastWriteAt !== null
                    && now - lastWriteAt < MIN_DURATION_MS) {
                    decoded.stats.pendingPresenterDrops++;
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
                    presented = true;
                } finally {
                    if (!presented) {
                        decoded.stats.pendingPresenterDrops++;
                    }
                    else {
                        aggregateDropTrace(decoded.stats, decoded.dropTrace);
                        if (decoded.stats.pendingPresenterDrops > 0) {
                            decoded.stats.dropTrace.set(
                                FrameDropStage.ReceiverPresent,
                                (decoded.stats.dropTrace.get(FrameDropStage.ReceiverPresent) ?? 0)
                                    + decoded.stats.pendingPresenterDrops);
                            decoded.stats.pendingPresenterDrops = 0;
                        }
                        decoded.stats.presented++;
                        updatePlaybackRateEma(decoded.stats, decoded.capturedAt, performance.now());
                    }
                }
                lastWriteAt = nextWriteAt;
                prevCapturedAt = decoded.capturedAt.timeMs;
                prevCapturedEpoch = decoded.capturedAt.epoch;
            } finally {
                try { frame.close(); } catch { /* already closed */ }
            }
        }
    }
}

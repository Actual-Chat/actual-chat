import { type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import type { NormalizedFrame } from '../frame-envelopes';
import { drawFrameCover, resizeCanvas } from '../canvas/resize';

const { warnLog } = getLogs('VideoPipeline');

export interface SimpleBlurOptions {
    radiusPx?: number;
}

// Temporary test effect for validating the post-normalize preview/effects
// boundary. Consumes the input frame and emits a same-sized blurred frame.
export function simpleBlur(opts: SimpleBlurOptions = {}): PipeOperator<NormalizedFrame, NormalizedFrame> {
    const radiusPx = opts.radiusPx ?? 6;
    const canvas = new OffscreenCanvas(0, 0);
    const ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
    if (!ctx)
        throw new Error('simpleBlur: 2D context unavailable on OffscreenCanvas');

    return source => (async function* (): AsyncIterable<NormalizedFrame> {
        try {
            for await (const envelope of source) {
                let output: VideoFrame | null = null;
                let mustCloseInput = true;
                let mustCloseOutput = false;
                try {
                    const input = envelope.frame;
                    const width = input.codedWidth;
                    const height = input.codedHeight;
                    resizeCanvas(canvas, width, height);

                    ctx.filter = `blur(${radiusPx}px)`;
                    try {
                        drawFrameCover(ctx, input, width, height);
                    } finally {
                        ctx.filter = 'none';
                    }
                    output = new VideoFrame(canvas, {
                        timestamp: input.timestamp,
                        duration: input.duration ?? undefined,
                        alpha: 'discard',
                    });
                    mustCloseOutput = true;
                    try { input.close(); } catch { /* already closed */ }
                    mustCloseInput = false;

                    const next: NormalizedFrame = { ...envelope, frame: output };
                    mustCloseOutput = false;
                    yield next;
                } catch (e) {
                    warnLog?.log('simpleBlur: process failed:', e);
                    throw e instanceof Error ? e : new Error(String(e));
                } finally {
                    if (mustCloseInput)
                        try { envelope.frame.close(); } catch { /* ignore */ }
                    if (mustCloseOutput && output)
                        try { output.close(); } catch { /* ignore */ }
                }
            }
        } finally {
            ctx.filter = 'none';
            resizeCanvas(canvas, 0, 0);
        }
    })() as unknown as ReturnType<PipeOperator<NormalizedFrame, NormalizedFrame>>;
}

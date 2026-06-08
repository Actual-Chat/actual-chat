// Canvas2D fallback downscaler — used when WebGL2 is unavailable or its
// context was lost. Same contract and cascade as the WebGL path: top tier is
// the normalized input (passed through), lower tiers are real pixel
// downscales (coded == target), each sampled from the previous larger tier.
//
// The input is already normalized (crop/rotation baked upstream by
// normalizeDownscale's ceiling render), so this is a pure aspect-1:1 downscale —
// no crop/rotate. The input is owned by the caller — never closed here.

import { getLogs } from 'logging';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { warnLog } = getLogs('VideoPipeline');

export class CanvasDownscaler implements DownscalerLike {
    private canvas: OffscreenCanvas | null = null;
    private ctx: OffscreenCanvasRenderingContext2D | null = null;

    // eslint-disable-next-line @typescript-eslint/require-await
    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const topIdx = layers.length - 1;
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        const inW = input.displayWidth || input.codedWidth;
        const inH = input.displayHeight || input.codedHeight;
        try {
            const ctx = this.ensureCtx();
            const canvas = this.canvas!;
            let src: CanvasImageSource = input;
            for (let i = topIdx; i >= 0; i--) {
                const { width, height } = layers[i];
                if (width === inW && height === inH) {
                    results[i] = input;
                    continue;
                }
                if (canvas.width !== width)
                    canvas.width = width;
                if (canvas.height !== height)
                    canvas.height = height;
                ctx.drawImage(src, 0, 0, width, height);
                const out = new VideoFrame(canvas, {
                    timestamp: input.timestamp,
                    alpha: 'discard',
                });
                results[i] = out;
                src = out;
            }
            return results as VideoFrame[];
        } catch (e) {
            warnLog?.log('CanvasDownscaler.process failed:', e);
            for (const r of results)
                if (r && r !== input) try { r.close(); } catch { /* ignore */ }
            throw e;
        }
    }

    dispose(): void {
        if (this.canvas) {
            this.canvas.width = 0;
            this.canvas.height = 0;
        }
        this.canvas = null;
        this.ctx = null;
    }

    private ensureCtx(): OffscreenCanvasRenderingContext2D {
        if (this.ctx)
            return this.ctx;
        this.canvas = new OffscreenCanvas(0, 0);
        const ctx = this.canvas.getContext('2d', { alpha: false, desynchronized: true });
        if (!ctx)
            throw new Error('CanvasDownscaler: 2D context unavailable on OffscreenCanvas');
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';
        this.ctx = ctx;
        return ctx;
    }
}

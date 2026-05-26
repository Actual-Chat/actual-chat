import { BgBlurPerfTracker } from '../services/bg-blur-stats';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoWebGPU');

// Filter values lifted from the pre-removal main-thread CanvasBgRenderTarget
// (commit before 2bbf4c02e). `saturate(1.2)` keeps the bleed colored rather
// than washed out under heavy blur. Overdraw pushes the filter's edge falloff
// off-canvas so CSS object-cover never reveals it.
const BG_FILTER = 'blur(3px) saturate(1.2)';
const BG_OVERDRAW_PX = 4;

// Off-thread bg backdrop using Canvas2D + `filter: blur(...)`. Fallback for
// browsers without WebGPU (Firefox, locked-down Safari) or when WebGPU init
// fails. Draws directly from a worker-side VideoFrame into a transferred
// OffscreenCanvas — no `<video>` element, no main-thread compositor sync.
// Aesthetically the same as the old main-thread WebGL Gaussian; the win is
// it costs ~1 ms instead of ~24 ms and never blocks UI.
export class Canvas2DBgRenderer {
    private readonly ctx: OffscreenCanvasRenderingContext2D | null;
    private readonly perf = new BgBlurPerfTracker('canvas2d');
    private disposed = false;

    constructor(private readonly canvas: OffscreenCanvas) {
        const ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
        if (!ctx) {
            warnLog?.log('Canvas2DBgRenderer: getContext("2d") returned null');
            this.ctx = null;
            return;
        }
        this.ctx = ctx;
        this.ctx.filter = BG_FILTER;
        this.ctx.imageSmoothingEnabled = true;
        this.ctx.imageSmoothingQuality = 'medium';
    }

    // Same shape as BgBlurRenderer.render. `strength` is ignored — Canvas2D
    // filter blur is fixed-radius; rolling it via the filter string per call
    // would cost a layer rebuild.
    render(frame: VideoFrame): boolean {
        if (this.disposed || !this.ctx) return false;
        const w = frame.displayWidth;
        const h = frame.displayHeight;
        if (w <= 0 || h <= 0) return false;
        const bgW = this.canvas.width;
        const bgH = this.canvas.height;
        try {
            const t0 = performance.now();
            this.ctx.drawImage(
                frame,
                -BG_OVERDRAW_PX,
                -BG_OVERDRAW_PX,
                bgW + 2 * BG_OVERDRAW_PX,
                bgH + 2 * BG_OVERDRAW_PX);
            this.perf.sample(performance.now() - t0);
            return true;
        } catch (e) {
            warnLog?.log('Canvas2DBgRenderer render failed:', e);
            return false;
        }
    }

    dispose(): void {
        this.disposed = true;
    }
}

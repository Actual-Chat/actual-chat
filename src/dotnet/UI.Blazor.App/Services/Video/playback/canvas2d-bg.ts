import { BgBlurPerfTracker } from '../services/bg-blur-stats';
import { bgFrameHeight, bgFrameWidth, type BgFrameSource } from './bg-blur-tap';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoWebGPU');

// Lifted from Dmitrii Filippov's 45f32d675 (2026-04-22) main-thread
// CanvasTarget — the bitmap-baked blur he benchmarked at ~50% paint cost
// reduction vs. CSS filter:blur. Same numbers here; the only structural
// change is that the canvas lives in the player worker and the source
// is a VideoFrame instead of a `<video>` element on main.
const BG_CANVAS_WIDTH = 64;
const BG_FILTER = 'blur(3px) saturate(1.3)';

// Off-thread bg backdrop via Canvas2D + `filter: blur(...)` baked into a
// low-res bitmap. drawImage stretches the whole frame down to a 64-px-wide
// canvas sized to the source aspect, with the blur applied during the
// downsample. CSS object-cover then upscales the result to fill the tile.
// Reads as a soft frosted backdrop of the whole frame.
//
// Fallback for browsers without WebGPU (Firefox, locked-down Safari) and
// for the explicit `?bgBlur=canvas2d` override. Draws directly from a
// worker-side VideoFrame into a transferred OffscreenCanvas — no `<video>`
// element, no main-thread compositor sync.
//
// Geometry note: tuned for the common portrait-source-on-landscape-panel
// case (left/right letterbox bars). Landscape-source-on-portrait-panel
// (top/bottom bars) gets the same left/right colors filling top/bottom
// bars; still coherent, just doesn't track the source's vertical edges.
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
        this.applyCtxState();
    }

    // Same shape as BgBlurRenderer.render. `strength` is ignored.
    render(frame: BgFrameSource): boolean {
        if (this.disposed || !this.ctx) return false;
        const fw = bgFrameWidth(frame);
        const fh = bgFrameHeight(frame);
        if (fw <= 0 || fh <= 0) return false;
        const targetW = BG_CANVAS_WIDTH;
        const targetH = Math.max(1, Math.round(targetW * fh / fw));
        try {
            const t0 = performance.now();
            if (this.canvas.width !== targetW || this.canvas.height !== targetH) {
                this.canvas.width = targetW;
                this.canvas.height = targetH;
                // Canvas resize resets ctx state (filter, smoothing) on every engine.
                this.applyCtxState();
            }
            this.ctx.drawImage(frame, 0, 0, targetW, targetH);
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

    private applyCtxState(): void {
        if (!this.ctx) return;
        this.ctx.filter = BG_FILTER;
        this.ctx.imageSmoothingEnabled = true;
        this.ctx.imageSmoothingQuality = 'medium';
    }
}

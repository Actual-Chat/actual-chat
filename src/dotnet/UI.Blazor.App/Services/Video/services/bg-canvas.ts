// Blurred backdrop drawn behind a contain-fitted main video tile
// (.remote-video-bg in video-panel.css). Receiver path: paints a raw
// downscaled frame on a 100 ms tick; the blur+saturate are applied by
// CSS `backdrop-filter` on a sibling overlay (works on iOS Safari, where
// Canvas 2D `ctx.filter` is silently dropped). Sender preview path (in
// recorder-preview-view.ts) still uses BG_FILTER baked into the bitmap.

import { CanvasTarget } from './canvas-target';

export const BG_CANVAS_WIDTH = 64;
export const BG_RECEIVER_CANVAS_WIDTH = 128;
export const BG_DRAW_INTERVAL_MS = 100;
export const BG_FILTER = 'blur(3px) saturate(1.2)';
export const BG_CANVAS_OVERDRAW_PX = 4;
// Reserved for shader-based blur paths (Kawase / WebGPU); the simple
// 2D-canvas variant below uses BG_FILTER directly.
export const BG_BLUR_STRENGTH = 20;

/** Drives the receiver .remote-video-bg canvas: every 100 ms reads the
 *  supplied source element (the visible `<video>` or `<canvas>`), draws
 *  it scaled down (no ctx.filter), then lets CSS `object-fit: cover`
 *  stretch the low-res bitmap to fill the focused tile's letterbox bars.
 *  A sibling overlay (.remote-video-bg-overlay) applies `backdrop-filter:
 *  blur(...) saturate(...)` over the canvas. */
export class BgCanvasPainter {
    private readonly target: CanvasTarget;
    private timer: ReturnType<typeof setInterval> | null = null;
    private getSource: (() => SourceLike | null) | null = null;

    constructor(bgCanvas: HTMLCanvasElement) {
        this.target = new CanvasTarget(bgCanvas, true, 'none', BG_CANVAS_OVERDRAW_PX);
    }

    /** Start or restart the pump. `getSource` is re-read every tick so a
     *  late-arriving `<video>` track or canvas swap is picked up live. */
    start(getSource: () => SourceLike | null): void {
        this.getSource = getSource;
        this.stop();
        this.timer = setInterval(() => this.tick(), BG_DRAW_INTERVAL_MS);
        this.tick();
    }

    stop(): void {
        if (this.timer !== null) {
            clearInterval(this.timer);
            this.timer = null;
        }
        this.target.clear();
    }

    dispose(): void {
        this.stop();
        this.getSource = null;
    }

    private tick(): void {
        const src = this.getSource?.();
        if (!src) return;
        const sw = readSourceWidth(src);
        const sh = readSourceHeight(src);
        if (sw <= 0 || sh <= 0) return;
        const bgW = BG_RECEIVER_CANVAS_WIDTH;
        const bgH = Math.max(1, Math.round(bgW * sh / sw));
        this.target.draw(src as CanvasImageSource, bgW, bgH);
    }
}

export type SourceLike = HTMLCanvasElement | HTMLVideoElement;

function readSourceWidth(src: SourceLike): number {
    if (src instanceof HTMLVideoElement) return src.videoWidth;
    return src.width;
}

function readSourceHeight(src: SourceLike): number {
    if (src instanceof HTMLVideoElement) return src.videoHeight;
    return src.height;
}

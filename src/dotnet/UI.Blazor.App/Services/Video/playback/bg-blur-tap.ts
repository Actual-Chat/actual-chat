import { tap, type PipeOperator } from 'ix-ext';
import type { DecodedFrame } from '../frame-envelopes';
import { BgBlurRenderer } from '../webgpu/blur';
import { Canvas2DBgRenderer } from './canvas2d-bg';
import { WebGlBgRenderer } from './webgl-bg';
import { getLogs } from 'logging';

const { infoLog, warnLog } = getLogs('VideoWebGPU');

// Receiver backdrop strength (WebGPU path only). Drives both the pyramid
// depth (≥20 → 4 levels) and the per-pass sample spread (offset =
// strength/textureSize). 20 was the value used by the pre-2bbf4c02e bg
// painter on this same shader.
const BG_BLUR_STRENGTH = 20;

// Repaint cadence. Backdrop is decorative letterbox fill — 10 Hz reads as
// smooth to a viewer who's watching the main video. Matches the old WebGL
// painter's BG_DRAW_INTERVAL_MS.
const BG_RENDER_INTERVAL_MS = 100;

// Renderer preference. Sent from main via installBgCanvas; the worker uses
// it as a hint on top of its own WebGPU probe.
//  • 'auto'     — pick WebGPU if available in this worker, else Canvas2D.
//  • 'webgpu'   — force WebGPU. Falls back to Canvas2D if construction throws.
//  • 'webgl'    — force WebGL2 separable-Gaussian (off-thread, gregblur-derived).
//  • 'canvas2d' — force Canvas2D (ctx.filter blur baked into a 64×N bitmap).
export type BgBlurMode = 'auto' | 'webgpu' | 'webgl' | 'canvas2d';

interface BgRenderer {
    render(frame: VideoFrame, strength?: number): boolean;
    dispose(): void;
}

function isWebGpuLikelyAvailableInWorker(): boolean {
    try {
        const nav = globalThis.navigator as Navigator | undefined;
        if (!nav || !('gpu' in nav))
            return false;
        return typeof GPUDevice !== 'undefined'
            && typeof GPUDevice.prototype.importExternalTexture === 'function';
    } catch {
        return false;
    }
}

// Per-stream owner of an OffscreenCanvas + bg renderer. Lifetime is bound
// to a single Player run (install on start, dispose when the player drains).
// The main thread sends the canvas across via player-worker RPC; the
// controller decides whether each decoded frame is fed through the renderer
// based on an `active` flag toggled by the main thread.
//
// Frame ownership: `maybeRender` reads envelope.frame WITHOUT taking it.
// The WebGPU renderer clones+defer-closes internally; Canvas2D drawImage
// reads synchronously, so the upstream pipe always keeps its frame.
export class BgBlurController {
    private renderer: BgRenderer | null = null;
    private rendererKind: 'webgpu' | 'webgl' | 'canvas2d' | null = null;
    private active = false;
    private lastRenderAtMs = 0;

    install(canvas: OffscreenCanvas, mode: BgBlurMode = 'auto'): void {
        if (this.renderer) {
            this.renderer.dispose();
            this.renderer = null;
            this.rendererKind = null;
        }
        const wantWebGpu = mode === 'webgpu'
            || (mode === 'auto' && isWebGpuLikelyAvailableInWorker());
        try {
            if (wantWebGpu) {
                this.renderer = new BgBlurRenderer(canvas);
                this.rendererKind = 'webgpu';
            } else if (mode === 'webgl') {
                this.renderer = new WebGlBgRenderer(canvas);
                this.rendererKind = 'webgl';
            } else {
                this.renderer = new Canvas2DBgRenderer(canvas);
                this.rendererKind = 'canvas2d';
            }
            infoLog?.log(`BgBlurController: installed ${this.rendererKind} renderer (mode=${mode})`);
        } catch (e) {
            warnLog?.log(`BgBlurController: ${this.rendererKind ?? 'primary'} ctor failed, falling back to Canvas2D:`, e);
            try {
                this.renderer = new Canvas2DBgRenderer(canvas);
                this.rendererKind = 'canvas2d';
            } catch (e2) {
                warnLog?.log('BgBlurController: Canvas2D ctor also failed:', e2);
                this.renderer = null;
                this.rendererKind = null;
            }
        }
    }

    setActive(active: boolean): void {
        this.active = active;
    }

    maybeRender(envelope: DecodedFrame): void {
        if (!this.active) return;
        const r = this.renderer;
        if (!r) return;
        const nowMs = performance.now();
        if (nowMs - this.lastRenderAtMs < BG_RENDER_INTERVAL_MS) return;
        this.lastRenderAtMs = nowMs;
        try {
            r.render(envelope.frame, BG_BLUR_STRENGTH);
        } catch (e) {
            warnLog?.log('BgBlurController: render failed:', e);
        }
    }

    dispose(): void {
        this.renderer?.dispose();
        this.renderer = null;
        this.rendererKind = null;
        this.active = false;
    }
}

export function bgBlurTap(controller: BgBlurController): PipeOperator<DecodedFrame, DecodedFrame> {
    return tap<DecodedFrame>(envelope => controller.maybeRender(envelope));
}

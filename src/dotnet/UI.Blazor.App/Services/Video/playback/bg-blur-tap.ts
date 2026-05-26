import { tap, type PipeOperator } from 'ix-ext';
import type { DecodedFrame } from '../frame-envelopes';
import { BgBlurRenderer } from '../webgpu/blur';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoWebGPU');

// Receiver backdrop strength. Drives both the pyramid depth (≥20 → 4 levels)
// and the per-pass sample spread (offset = strength/textureSize). The default
// applyFullFrameBlur strength of 4 produces a barely-blurred pyramid mip 0
// that aliases when the composite shader downsamples it onto the small
// (64×64) bg canvas — the result looks pixelated through CSS object-cover.
// 20 was the value used by the pre-2bbf4c02e bg painter on this same shader.
const BG_BLUR_STRENGTH = 20;

// Per-stream owner of an OffscreenCanvas + its BgBlurRenderer. Lifetime is
// bound to a single Player run (install on start, dispose when the player
// drains). The main thread sends the canvas across via the player-worker
// RPC; the controller decides whether each decoded frame is fed through
// the GPU pyramid based on an `active` flag toggled by the main thread.
//
// Frame ownership: `maybeRender` reads envelope.frame WITHOUT taking
// ownership. `BgBlurRenderer.render` internally clones the frame and
// closes the clone via the deferred-cleanup queue, so the upstream
// envelope keeps flowing through the IxJS pipeline unmodified.
export class BgBlurController {
    private renderer: BgBlurRenderer | null = null;
    private active = false;

    install(canvas: OffscreenCanvas): void {
        if (this.renderer) {
            this.renderer.dispose();
            this.renderer = null;
        }
        try {
            this.renderer = new BgBlurRenderer(canvas);
        } catch (e) {
            warnLog?.log('BgBlurController: BgBlurRenderer construction failed:', e);
            this.renderer = null;
        }
    }

    setActive(active: boolean): void {
        this.active = active;
    }

    maybeRender(envelope: DecodedFrame): void {
        if (!this.active) return;
        const r = this.renderer;
        if (!r) return;
        try {
            r.render(envelope.frame, BG_BLUR_STRENGTH);
        } catch (e) {
            warnLog?.log('BgBlurController: render failed:', e);
        }
    }

    dispose(): void {
        this.renderer?.dispose();
        this.renderer = null;
        this.active = false;
    }
}

export function bgBlurTap(controller: BgBlurController): PipeOperator<DecodedFrame, DecodedFrame> {
    return tap<DecodedFrame>(envelope => controller.maybeRender(envelope));
}

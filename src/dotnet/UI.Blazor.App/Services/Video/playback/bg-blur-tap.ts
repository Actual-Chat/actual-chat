import { tap, type PipeOperator } from 'ix-ext';
import type { DecodedFrame } from '../frame-envelopes';
import { BgBlurRenderer } from '../webgpu/blur';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoWebGPU');

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
            r.render(envelope.frame);
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

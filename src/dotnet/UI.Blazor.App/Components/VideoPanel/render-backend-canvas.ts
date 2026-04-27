import { getLogs } from 'logging';
import type { PresentableFrame, RenderBackend } from './render-backend';

const { debugLog, errorLog } = getLogs('VideoPlayer');

export class CanvasRenderBackend implements RenderBackend {
    readonly kind = 'canvas' as const;
    readonly isOffThread = false;
    private readonly ctx: CanvasRenderingContext2D | null;

    constructor(private readonly canvas: HTMLCanvasElement) {
        this.ctx = canvas.getContext('2d');
    }

    drawFrame(pf: PresentableFrame): void {
        if (!this.ctx) return;
        try {
            if (this.canvas.width !== pf.displayWidth || this.canvas.height !== pf.displayHeight) {
                this.canvas.width = pf.displayWidth;
                this.canvas.height = pf.displayHeight;
                debugLog?.log(`Canvas resized to ${pf.displayWidth}x${pf.displayHeight}`);
            }
            this.ctx.drawImage(pf.drawable as CanvasImageSource, 0, 0);
        } catch (error) {
            errorLog?.log('Error rendering frame:', error);
        }
    }

    dispose(): void {
        // Canvas element ownership stays with Blazor; nothing to release.
    }
}

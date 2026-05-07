import { getLogs } from 'logging';
import type { PresentableFrame, RenderBackend } from './render-backend';

const { debugLog, errorLog } = getLogs('VideoPlayer');

export class CanvasRenderBackend implements RenderBackend {
    readonly kind = 'canvas' as const;
    readonly isOffThread = false;
    private readonly ctx: CanvasRenderingContext2D | null;
    // Cached aspect-ratio string applied to the parent container. Skip the
    // DOM write when the live frame dims still resolve to the same ratio —
    // touching `style.aspectRatio` triggers a re-flow even when the value is
    // identical.
    private lastAspectRatio = '';
    private lastOutputSize: { width: number; height: number } | null = null;

    constructor(private readonly canvas: HTMLCanvasElement) {
        this.ctx = canvas.getContext('2d');
    }

    getOutputSize(): { width: number; height: number } | null {
        return this.lastOutputSize;
    }

    drawFrame(pf: PresentableFrame): void {
        if (!this.ctx) return;
        try {
            if (this.canvas.width !== pf.displayWidth || this.canvas.height !== pf.displayHeight) {
                this.canvas.width = pf.displayWidth;
                this.canvas.height = pf.displayHeight;
                debugLog?.log(`Canvas resized to ${pf.displayWidth}x${pf.displayHeight}`);
                this.applyContainerAspect(pf.displayWidth, pf.displayHeight);
            }
            this.lastOutputSize = { width: pf.displayWidth, height: pf.displayHeight };
            this.ctx.drawImage(pf.drawable as CanvasImageSource, 0, 0);
        } catch (error) {
            errorLog?.log('Error rendering frame:', error);
        }
    }

    // The Razor `Format`-derived aspect-ratio is a pre-first-frame guess
    // (and is frozen even on mid-stream rotation, since VideoStreamInfo is
    // not republished). Override the parent container's inline aspect-ratio
    // here, where we know the live pipe's true dims. Both the canvas and
    // the sibling <video> element share the same parent, so a single write
    // covers both. Falls through silently if the parent is detached.
    private applyContainerAspect(width: number, height: number): void {
        if (width <= 0 || height <= 0) return;
        const ratio = `${width} / ${height}`;
        if (ratio === this.lastAspectRatio) return;
        const parent = this.canvas.parentElement;
        if (!parent) return;
        parent.style.aspectRatio = ratio;
        this.lastAspectRatio = ratio;
        debugLog?.log(`Container aspect-ratio set to ${ratio}`);
    }

    dispose(): void {
        // Canvas element ownership stays with Blazor; nothing to release.
    }
}

export class TransferableCanvasRenderBackend implements RenderBackend {
    readonly kind = 'canvas' as const;
    readonly isOffThread = false;

    constructor(private readonly canvas: HTMLCanvasElement) {}

    getOutputSize(): { width: number; height: number } | null {
        if (this.canvas.width <= 0 || this.canvas.height <= 0)
            return null;
        return { width: this.canvas.width, height: this.canvas.height };
    }

    drawFrame(pf: PresentableFrame): void {
        const disposable = pf.drawable as unknown as { close?: () => void };
        try { disposable.close?.(); } catch { /* ignore */ }
    }

    dispose(): void {
        // Canvas element ownership stays with Blazor; nothing to release.
    }
}

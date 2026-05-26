import { getLogs } from 'logging';
import type { PresentableFrame, RenderBackend } from './render-backend';
import { applyRotationLayout } from '../../Services/Video/services/tile-fit';

const { debugLog, errorLog } = getLogs('VideoPlayer');

export class CanvasRenderBackend implements RenderBackend {
    readonly kind = 'canvas' as const;
    readonly isOffThread = false;
    private readonly ctx: CanvasRenderingContext2D | null;
    private lastAspectRatio = '';
    private lastOutputSize: { width: number; height: number } | null = null;
    private rotationQuarter = 0;
    private currentFit: 'cover' | 'contain' = 'cover';

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

    setRotation(quarter: number): void {
        const q = ((Math.round(quarter) % 4) + 4) % 4;
        if (q === this.rotationQuarter) return;
        this.rotationQuarter = q;
        applyRotationLayout(this.canvas, q);
        if (this.lastOutputSize)
            this.applyContainerAspect(this.lastOutputSize.width, this.lastOutputSize.height, true);
    }

    recomputeLayout(): void {
        // Odd quarters layout-size the canvas from parent dims; re-apply on resize.
        if ((this.rotationQuarter & 1) === 1)
            applyRotationLayout(this.canvas, this.rotationQuarter);
    }

    setFit(fit: 'cover' | 'contain'): void {
        if (fit === this.currentFit) return;
        this.currentFit = fit;
        this.canvas.style.objectFit = fit;
    }

    // No-op: bg blur runs in the player worker. See VideoPlayer.applyBackdrop.
    setBackdrop(_canvas: HTMLCanvasElement | null, _focused: boolean): void {
        // intentionally empty
    }

    setExpectedPaused(_paused: boolean): void {
        // Canvas backends have no stall watchdog — VideoPlayer's main-thread
        // draw loop already gates on frame arrival.
    }

    private applyContainerAspect(width: number, height: number, force = false): void {
        if (width <= 0 || height <= 0) return;
        // 90/270 turns swap visual W/H, so the parent's aspect-ratio must invert.
        const swap = (this.rotationQuarter & 1) === 1;
        const visibleW = swap ? height : width;
        const visibleH = swap ? width : height;
        const ratio = `${visibleW} / ${visibleH}`;
        if (!force && ratio === this.lastAspectRatio) return;
        const parent = this.canvas.parentElement;
        if (!parent) return;
        parent.style.aspectRatio = ratio;
        this.lastAspectRatio = ratio;
        debugLog?.log(`Container aspect-ratio set to ${ratio} (rotation=${this.rotationQuarter})`);
    }

    dispose(): void {
        // intentionally empty
    }
}

export class TransferableCanvasRenderBackend implements RenderBackend {
    readonly kind = 'canvas' as const;
    readonly isOffThread = false;
    private rotationQuarter = 0;
    private lastAspectRatio = '';
    private currentFit: 'cover' | 'contain' = 'cover';

    constructor(private readonly canvas: HTMLCanvasElement) {}

    getOutputSize(): { width: number; height: number } | null {
        if (this.canvas.width <= 0 || this.canvas.height <= 0)
            return null;
        return { width: this.canvas.width, height: this.canvas.height };
    }

    drawFrame(pf: PresentableFrame): void {
        // OffscreenCanvas owns rendering; we still need to track dims to
        // keep parent aspect-ratio + rotation transform in sync.
        if (this.canvas.width > 0 && this.canvas.height > 0)
            this.applyContainerAspect(this.canvas.width, this.canvas.height);
        const disposable = pf.drawable as unknown as { close?: () => void };
        try { disposable.close?.(); } catch { /* ignore */ }
    }

    setRotation(quarter: number): void {
        const q = ((Math.round(quarter) % 4) + 4) % 4;
        if (q === this.rotationQuarter) return;
        this.rotationQuarter = q;
        applyRotationLayout(this.canvas, q);
        if (this.canvas.width > 0 && this.canvas.height > 0)
            this.applyContainerAspect(this.canvas.width, this.canvas.height, true);
    }

    recomputeLayout(): void {
        if ((this.rotationQuarter & 1) === 1)
            applyRotationLayout(this.canvas, this.rotationQuarter);
    }

    setFit(fit: 'cover' | 'contain'): void {
        if (fit === this.currentFit) return;
        this.currentFit = fit;
        this.canvas.style.objectFit = fit;
    }

    // No-op: bg blur runs in the player worker. See VideoPlayer.applyBackdrop.
    setBackdrop(_canvas: HTMLCanvasElement | null, _focused: boolean): void {
        // intentionally empty
    }

    setExpectedPaused(_paused: boolean): void {
        // Canvas backends have no stall watchdog — VideoPlayer's main-thread
        // draw loop already gates on frame arrival.
    }

    private applyContainerAspect(width: number, height: number, force = false): void {
        const swap = (this.rotationQuarter & 1) === 1;
        const visibleW = swap ? height : width;
        const visibleH = swap ? width : height;
        const ratio = `${visibleW} / ${visibleH}`;
        if (!force && ratio === this.lastAspectRatio) return;
        const parent = this.canvas.parentElement;
        if (!parent) return;
        parent.style.aspectRatio = ratio;
        this.lastAspectRatio = ratio;
    }

    dispose(): void {
        // intentionally empty
    }
}

/**
 * CanvasTarget
 * Thin wrapper around HTMLCanvasElement that caches its 2D context and
 * handles the common "resize to source + drawImage" pattern.
 */

export class CanvasTarget {
    private readonly ctx: CanvasRenderingContext2D;

    constructor(public readonly element: HTMLCanvasElement) {
        this.ctx = element.getContext('2d')!;
    }

    /**
     * Resize the canvas to the given dimensions (if changed) and draw the source scaled to fill.
     * No-op when width or height is zero.
     */
    draw(source: CanvasImageSource, width: number, height: number): void {
        if (width <= 0 || height <= 0) return;
        if (this.element.width !== width || this.element.height !== height) {
            this.element.width = width;
            this.element.height = height;
        }
        this.ctx.drawImage(source, 0, 0, width, height);
    }

    /**
     * Wipe all pixels so the canvas shows transparent (letting the container background show
     * through). Call when detaching — otherwise the last-drawn frame stays visible.
     */
    clear(): void {
        this.ctx.clearRect(0, 0, this.element.width, this.element.height);
    }
}

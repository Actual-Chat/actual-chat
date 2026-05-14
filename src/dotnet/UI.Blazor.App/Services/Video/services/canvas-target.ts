export class CanvasTarget {
    private readonly ctx: CanvasRenderingContext2D;
    private readonly smoothing: boolean;
    private readonly filter: string;
    private readonly overdrawPx: number;

    constructor(public readonly element: HTMLCanvasElement, smoothing = true, filter = 'none', overdrawPx = 0) {
        this.ctx = element.getContext('2d')!;
        this.smoothing = smoothing;
        this.filter = filter;
        this.overdrawPx = Math.max(0, overdrawPx);
        this.applyCtxState();
    }

    draw(source: CanvasImageSource, width: number, height: number, mirrorX = false): void {
        if (width <= 0 || height <= 0) return;
        if (this.element.width !== width || this.element.height !== height) {
            this.element.width = width;
            this.element.height = height;
            // Canvas resize resets context state.
            this.applyCtxState();
        }
        const overdrawPx = this.overdrawPx;
        this.ctx.save();
        try {
            if (mirrorX) {
                this.ctx.translate(width, 0);
                this.ctx.scale(-1, 1);
            }
            this.ctx.drawImage(
                source,
                -overdrawPx,
                -overdrawPx,
                width + 2 * overdrawPx,
                height + 2 * overdrawPx);
        } finally {
            this.ctx.restore();
        }
    }

    private applyCtxState(): void {
        if (!this.smoothing)
            this.ctx.imageSmoothingEnabled = false;
        if (this.filter !== 'none')
            this.ctx.filter = this.filter;
    }

    clear(): void {
        this.ctx.clearRect(0, 0, this.element.width, this.element.height);
    }
}

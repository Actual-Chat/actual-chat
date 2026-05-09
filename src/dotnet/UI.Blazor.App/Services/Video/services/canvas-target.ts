export class CanvasTarget {
    private readonly ctx: CanvasRenderingContext2D;
    private readonly smoothing: boolean;
    private readonly filter: string;

    constructor(public readonly element: HTMLCanvasElement, smoothing = true, filter = 'none') {
        this.ctx = element.getContext('2d')!;
        this.smoothing = smoothing;
        this.filter = filter;
        this.applyCtxState();
    }

    draw(source: CanvasImageSource, width: number, height: number): void {
        if (width <= 0 || height <= 0) return;
        if (this.element.width !== width || this.element.height !== height) {
            this.element.width = width;
            this.element.height = height;
            // Canvas resize resets context state.
            this.applyCtxState();
        }
        this.ctx.drawImage(source, 0, 0, width, height);
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

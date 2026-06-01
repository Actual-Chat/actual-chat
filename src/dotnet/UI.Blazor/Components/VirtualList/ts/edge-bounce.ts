// Elastic edge magnet for an infinite list whose content edge is virtual (the discovered first/last
// item, mid-wrapper). A self-sustaining rAF loop reads live state every frame: resistance via a
// counter-transform while dragging, then eases scrollTop to the edge frame-by-frame once settled.

export interface EdgeBounceHost {
    // Signed overscroll past the discovered edge: >0 below the newest, <0 above the oldest, 0 = none.
    getOverscroll(): number;
    // Target scrollTop that puts the relevant edge flush, given the signed overscroll.
    getBoundary(over: number): number;
    getViewportHeight(): number;
    isDragging(): boolean;          // pointer/touch currently down
    getScrollTop(): number;
    setScrollTop(value: number): void;
    setTransform(y: number): void;  // translateY on the content layer
}

const ResistanceGain = 0.55;        // c: ~55% follow on the first pixels, tapering toward the viewport
const MagnetEase = 0.28;            // fraction of the remaining gap closed per frame once settled
const DoneEps = 0.5;                // px: gap below this is treated as closed

export class EdgeBounce {
    private raf: number | null = null;

    constructor(private readonly host: EdgeBounceHost) { }

    public get isActive(): boolean { return this.raf != null; }

    // Idempotent: start the loop if a gap exists; calling it again while running is a no-op.
    public engage(): void {
        if (this.raf != null || this.host.getOverscroll() === 0)
            return;

        const step = () => {
            const over = this.host.getOverscroll();
            if (over === 0) {
                this.host.setTransform(0);
                this.raf = null;
                return;
            }
            if (this.host.isDragging()) {
                const x = Math.abs(over);
                const d = this.host.getViewportHeight();
                const f = (x * d * ResistanceGain) / (d + ResistanceGain * x);
                this.host.setTransform(over - Math.sign(over) * f);
            }
            else {
                this.host.setTransform(0);
                const st = this.host.getScrollTop();
                const boundary = this.host.getBoundary(over);
                const next = st + (boundary - st) * MagnetEase;
                this.host.setScrollTop(Math.abs(boundary - next) < DoneEps ? boundary : next);
            }
            this.raf = requestAnimationFrame(step);
        };
        this.raf = requestAnimationFrame(step);
    }

    public reset(): void {
        if (this.raf != null) {
            cancelAnimationFrame(this.raf);
            this.raf = null;
        }
        this.host.setTransform(0);
    }
}

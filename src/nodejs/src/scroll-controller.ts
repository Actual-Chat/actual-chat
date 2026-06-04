import { clamp } from 'math';
import { fastRaf } from 'fast-raf';

export interface ScrollToOptions {
    smooth?: boolean;
    clamp?: boolean;
    offset?: number;
}

const FixPrecisionPx = 0.1;
const ProgrammaticScrollSuppressMs = 300;

// Touch-drag rubber band: marginal resistance ramps 0 -> MaxResistance over ResistanceRampPx of pull,
// so the first pixels move almost 1:1 and resistance grows as you drag further (soft, spring-like).
const MaxResistance = 0.667;
const ResistanceRampPx = 667;
// Predict scroll ~half a frame ahead (speed + acceleration) so the drag transform isn't a frame behind.
const ExtrapolationFraction = 0.5;
const MaxFrameMs = 32;

// Return spring (release / fling): critically damped => single excursion, no second bounce.
const ReturnStiffness = 120;
const ReturnDampingRatio = 1;
const MaxReturnSpeedPxS = 3000;
const ReturnSettlePx = 0.3;
const ReturnSettleSpeedPxS = 8;
const MaxFlingOverscrollPx = 120;       // cap on the spring excursion for a no-touch fling
const FlingEntryAsymptotePx = 100;      // smoothly caps the no-touch entry offset (no snap on a fast fling)

// Pull-back distance (the part the resistance "eats") for a touch overscroll of `over` (>= 0).
function resistancePull(over: number): number {
    return over <= ResistanceRampPx
        ? (MaxResistance * over * over) / (2 * ResistanceRampPx)
        : MaxResistance * over - (MaxResistance * ResistanceRampPx) / 2;
}

// Visible overscroll the finger sees for a touch overscroll of `over` (>= 0).
function visibleOverscroll(over: number): number {
    return over - resistancePull(over);
}

// No-touch entry offset: a fast fling can overshoot the boundary far in one frame; map it through an
// asymptote so the spring starts from a bounded offset (smoothly, no snap) and the speed drives the rest.
function flingEntryOffset(over: number): number {
    return (over * FlingEntryAsymptotePx) / (over + FlingEntryAsymptotePx);
}

// Binds to one scrollable element and, when scroll handling is enabled, keeps scrollTop within a
// [min, max] band (provided on demand via getLimits, so it reflects current item sizes / viewport) with
// an iOS-style rubber-band overscroll on bounceElement:
//  - finger down + past the boundary: a soft, ramping-resistance transform tracks the (latency-
//    compensated) overscroll; scroll is NOT blocked.
//  - no finger + past the boundary: scroll is blocked (overflow:hidden, scrollTop pinned to the edge)
//    and a critically-damped spring carries the view back to the edge with the entry speed and stops.
export class ScrollController {
    private static readonly all = new Set<ScrollController>();

    private readonly abort = new AbortController();

    private isTouching = false;
    private lastScrollTop = 0;
    private lastScrollTime = 0;
    private recentSpeed = 0;            // px/ms, signed, smoothed (no-touch path)
    private suppressUntil = 0;
    private isOverflowLocked = false;

    private overscrollSign = 0;         // +1 = past max (bottom), -1 = past min (top), 0 = in band
    private overscrollBoundary = 0;

    private touchLoopEpoch = 0;
    private isTouchLooping = false;
    private touchPrevTop = 0;
    private touchPrevTime = 0;
    private touchSpeed = 0;             // px/ms, smoothed
    private touchAccel = 0;             // px/ms^2, smoothed

    private isReturning = false;
    private returnEpoch = 0;
    private springOffset = 0;           // px, signed visible overscroll
    private springSpeed = 0;            // px/s
    private springCap = 0;              // px, max |springOffset| (graceful clamp)

    constructor(
        private readonly element: HTMLElement,
        private readonly enableScrollHandling = false,
        private readonly bounceElement: HTMLElement = element,
        private readonly getLimits: () => { min: number | null, max: number | null } = () => ({ min: null, max: null }),
    ) {
        if (this.enableScrollHandling) {
            this.lastScrollTop = element.scrollTop;
            this.lastScrollTime = Date.now();
            const opts = { passive: true, signal: this.abort.signal };
            element.addEventListener('scroll', () => this.onScroll(), opts);
            element.addEventListener('touchstart', () => this.onTouchStart(), opts);
            const onTouchEnd = (e: TouchEvent) => { if (e.touches.length === 0) this.onTouchEnd(); };
            element.addEventListener('touchend', onTouchEnd, opts);
            element.addEventListener('touchcancel', onTouchEnd, opts);
        }
        ScrollController.all.add(this);
        (element as unknown as { scrollController?: ScrollController }).scrollController = this;
    }

    public dispose(): void {
        this.finishReturn();
        ++this.touchLoopEpoch;
        this.abort.abort();
        ScrollController.all.delete(this);
    }

    public static stopScrollAll(): void {
        for (const controller of this.all)
            controller.stopMomentum();
    }

    public scrollTo(target: number | HTMLElement, options: ScrollToOptions = {}): void {
        this.suppressUntil = Date.now() + ProgrammaticScrollSuppressMs;
        if (typeof target === 'number') {
            let top = target + (options.offset ?? 0);
            if (options.clamp !== false) {
                const limits = this.getCurrentScrollLimits();
                top = clamp(top, limits.min, limits.max);
            }
            if (options.smooth === false) {
                this.element.scrollTop = top;
                void this.element.offsetHeight; // Triggers reflow
            }
            else
                this.element.scrollTo({ top, behavior: 'smooth' });
            return;
        }
        target.scrollIntoView({
            behavior: options.smooth === false ? 'instant' : 'smooth',
            block: 'nearest',
        });
    }

    // Re-clamp scrollTop to the current limits (e.g. after a render or a resize changed them).
    public clampToLimits(): void {
        if (!this.enableScrollHandling || this.isTouching || this.isReturning)
            return;

        const limits = this.getCurrentScrollLimits();
        const clamped = clamp(this.element.scrollTop, limits.min, limits.max);
        if (Math.abs(this.element.scrollTop - clamped) > FixPrecisionPx)
            this.scrollTo(clamped, { smooth: false });
    }

    public getCurrentScrollLimits(): { min: number, max: number } {
        // null edge => no limit that way (free scroll): ±Infinity, so the rubber-band and clamp never engage.
        const raw = this.getLimits();
        const min = raw.min ?? Number.NEGATIVE_INFINITY;
        const max = raw.max ?? Number.POSITIVE_INFINITY;
        return {
            min: min,
            max: max < min ? min : max,
        };
    }

    // Private methods

    private onScroll(): void {
        const now = Date.now();
        const scrollTop = this.element.scrollTop;
        const dt = now - this.lastScrollTime;
        if (!this.isTouching && !this.isReturning && now >= this.suppressUntil && dt > 0 && dt < 200)
            this.recentSpeed = 0.6 * (scrollTop - this.lastScrollTop) / dt + 0.4 * this.recentSpeed;
        else if (now < this.suppressUntil)
            this.recentSpeed = 0;
        this.lastScrollTop = scrollTop;
        this.lastScrollTime = now;

        // The touch loop drives the drag; the spring owns scrollTop while returning.
        if (this.isTouching || this.isReturning || now < this.suppressUntil)
            return;

        const boundary = this.overBoundary(scrollTop);
        if (boundary === null) {
            if (this.overscrollSign !== 0) {
                this.overscrollSign = 0;
                this.clearTransform();
            }
            return;
        }
        const over = scrollTop - boundary;
        this.overscrollSign = Math.sign(over);
        this.startReturn(boundary, this.overscrollSign * flingEntryOffset(Math.abs(over)), this.recentSpeed);
    }

    private onTouchStart(): void {
        this.isTouching = true;
        if (this.isReturning)
            this.finishReturn();
        this.startTouchLoop();
    }

    private onTouchEnd(): void {
        this.isTouching = false;
        const scrollTop = this.element.scrollTop;
        const boundary = this.overBoundary(scrollTop);
        if (boundary !== null) {
            this.overscrollSign = Math.sign(scrollTop - boundary);
            this.startReturn(boundary, this.overscrollSign * visibleOverscroll(Math.abs(scrollTop - boundary)), this.touchSpeed);
        }
        else if (this.overscrollSign !== 0) {
            this.overscrollSign = 0;
            this.clearTransform();
        }
    }

    private overBoundary(scrollTop: number): number | null {
        const limits = this.getCurrentScrollLimits();
        if (scrollTop < limits.min - FixPrecisionPx)
            return limits.min;
        if (scrollTop > limits.max + FixPrecisionPx)
            return limits.max;
        return null;
    }

    private startTouchLoop(): void {
        if (this.isTouchLooping)
            return;

        this.isTouchLooping = true;
        this.touchPrevTop = this.element.scrollTop;
        this.touchPrevTime = Date.now();
        this.touchSpeed = 0;
        this.touchAccel = 0;
        const epoch = ++this.touchLoopEpoch;
        const tick = () => {
            if (epoch !== this.touchLoopEpoch || !this.isTouching) {
                this.isTouchLooping = false;
                return;
            }

            const now = Date.now();
            const scrollTop = this.element.scrollTop;
            const dt = now - this.touchPrevTime;
            if (dt > 0) {
                const speed = (scrollTop - this.touchPrevTop) / dt;
                this.touchAccel = 0.6 * (speed - this.touchSpeed) / dt + 0.4 * this.touchAccel;
                this.touchSpeed = 0.6 * speed + 0.4 * this.touchSpeed;
                this.touchPrevTop = scrollTop;
                this.touchPrevTime = now;
            }
            const t = ExtrapolationFraction * Math.min(dt, MaxFrameMs);
            const predicted = scrollTop + this.touchSpeed * t + 0.5 * this.touchAccel * t * t;

            const limits = this.getCurrentScrollLimits();
            const sign = predicted < limits.min ? -1 : predicted > limits.max ? 1 : 0;
            if (sign !== 0) {
                const boundary = sign < 0 ? limits.min : limits.max;
                this.overscrollSign = sign;
                this.applyTranslate(sign * resistancePull(Math.abs(predicted - boundary)));
            }
            else if (this.overscrollSign !== 0) {
                this.overscrollSign = 0;
                this.clearTransform();
            }
            fastRaf({ write: tick });
        };
        fastRaf({ write: tick });
    }

    private startReturn(boundary: number, offset: number, speedPxMs: number): void {
        this.overscrollBoundary = boundary;
        this.springOffset = offset;
        this.springCap = Math.max(Math.abs(offset), MaxFlingOverscrollPx);
        this.springSpeed = clamp(speedPxMs * 1000, -MaxReturnSpeedPxS, MaxReturnSpeedPxS);
        this.setOverflowLocked(true);
        this.element.scrollTop = boundary;
        this.applyTranslate(-this.springOffset);
        if (this.isReturning)
            return;

        this.isReturning = true;
        const epoch = ++this.returnEpoch;
        let lastTime = Date.now();
        const tick = () => {
            if (epoch !== this.returnEpoch)
                return;

            const now = Date.now();
            const dt = Math.min((now - lastTime) / 1000, 1 / 30);
            lastTime = now;
            if (dt > 0) {
                const k = ReturnStiffness;
                const c = ReturnDampingRatio * 2 * Math.sqrt(k);
                this.springSpeed += (-k * this.springOffset - c * this.springSpeed) * dt;
                let next = this.springOffset + this.springSpeed * dt;
                if (next > this.springCap) { next = this.springCap; if (this.springSpeed > 0) this.springSpeed = 0; }
                else if (next < -this.springCap) { next = -this.springCap; if (this.springSpeed < 0) this.springSpeed = 0; }
                this.springOffset = next;
            }
            if (Math.abs(this.springOffset) <= ReturnSettlePx && Math.abs(this.springSpeed) <= ReturnSettleSpeedPxS) {
                this.finishReturn();
                return;
            }
            if (Math.abs(this.element.scrollTop - this.overscrollBoundary) > FixPrecisionPx)
                this.element.scrollTop = this.overscrollBoundary;
            this.applyTranslate(-this.springOffset);
            fastRaf({ write: tick });
        };
        fastRaf({ write: tick });
    }

    private finishReturn(): void {
        this.isReturning = false;
        this.springOffset = 0;
        this.springSpeed = 0;
        this.overscrollSign = 0;
        ++this.returnEpoch;
        this.clearTransform();
        this.setOverflowLocked(false);
    }

    private stopMomentum(): void {
        if (!this.enableScrollHandling || this.isReturning || this.isTouching)
            return;

        this.setOverflowLocked(true);
        void this.element.offsetHeight;
        fastRaf({ write: () => { if (!this.isReturning) this.setOverflowLocked(false); } });
    }

    private applyTranslate(y: number): void {
        this.bounceElement.style.transform = `translate3d(0, ${y}px, 0)`;
    }

    private clearTransform(): void {
        this.bounceElement.style.transform = '';
    }

    private setOverflowLocked(locked: boolean): void {
        if (this.isOverflowLocked === locked)
            return;

        this.isOverflowLocked = locked;
        this.element.style.overflowY = locked ? 'hidden' : '';
    }
}

(globalThis as unknown as { ScrollController: typeof ScrollController }).ScrollController = ScrollController;

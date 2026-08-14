import { clamp } from 'math';
import { fastRaf } from 'fast-raf';
import { DeviceInfo } from 'device-info';

export interface ScrollToOptions {
    smooth?: boolean;
    clamp?: boolean;
    offset?: number;
}

const FixPrecisionPx = 0.1;
const ProgrammaticScrollSuppressMs = 300;
// Sub-pixel, so it changes the layer's transform matrix without moving anything you can see.
const RepaintNudgeTransform = 'translate3d(0, 0.01px, 0)';

// Touch rubber-band: resistance ramps 0 -> MaxResistance over ResistanceRampPx of pull (soft, spring-like).
const MaxResistance = 0.667;
const ResistanceRampPx = 667;

// Release/fling return spring: critically damped (single excursion, no second bounce).
const ReturnStiffness = 120;
const ReturnDampingRatio = 1;
const MaxReturnSpeedPxS = 3000;
const ReturnSettlePx = 0.3;
const ReturnSettleSpeedPxS = 8;
const MaxFlingOverscrollPx = 120;
const FlingEntryAsymptotePx = 100;

// Keeps an element's scrollTop within getScrollLimits()'s [min, max] band with iOS-style rubber-band
// overscroll: finger-down past the edge tracks with ramping resistance (scroll not blocked); no-finger
// past the edge pins to the edge (overflow:hidden) and a critically-damped spring returns it.
export class ScrollController {
    private static readonly all = new Set<ScrollController>();

    private readonly abort = new AbortController();

    private isTouching = false;
    private lastScrollTop = 0;
    private lastScrollTime = 0;
    private recentSpeed = 0;            // px/ms, signed, smoothed (no-touch path)
    private suppressUntil = 0;
    private isOverflowLocked = false;
    private resizeObserver: ResizeObserver | null = null;

    private overscrollSign = 0;         // +1 = past max (bottom), -1 = past min (top), 0 = in band
    private overscrollBoundary = 0;

    private touchLoopEpoch = 0;
    private isTouchLooping = false;
    private touchPrevTop = 0;
    private touchPrevTime = 0;
    private touchSpeed = 0;             // px/ms, smoothed

    private isReturning = false;
    private returnEpoch = 0;
    private springOffset = 0;           // px, signed visible overscroll
    private springSpeed = 0;            // px/s
    private springCap = 0;              // px, max |springOffset| (graceful clamp)

    constructor(
        private readonly element: HTMLElement,
        private readonly enableScrollConstraints = false,
        private readonly overscrollElement: HTMLElement = element,
        private readonly getScrollLimits: () => { min: number | null, max: number | null } = () => ({ min: null, max: null }),
    ) {
        if (this.enableScrollConstraints) {
            this.lastScrollTop = element.scrollTop;
            this.lastScrollTime = Date.now();
            const opts = { passive: true, signal: this.abort.signal };
            element.addEventListener('scroll', () => this.onScroll(), opts);
            // Touch listeners go on `document`, not on `element`. On iOS, every element with a
            // touch-related listener makes WebKit walk that element's entire renderer subtree
            // once per rendering update to union its touch region; `document` is special-cased
            // to one whole-view rect. For a scroller wrapping a long list that walk is the
            // whole list, every frame. A touch is dispatched to where it STARTED, so a
            // document-level listener plus this containment test sees exactly the same touches
            // - including a finger that drifts off the scroller before it lifts.
            document.addEventListener('touchstart', (e: TouchEvent) => {
                if (e.target instanceof Node && element.contains(e.target))
                    this.onTouchStart();
            }, opts);
            // The end is gated on `isTouching` rather than on containment: a finger that
            // started here and lifted elsewhere must still end the gesture, but a touch that
            // never involved this scroller must not - onTouchEnd reads scrollTop and can start
            // an overscroll return, and every instance in the app now sees every touch end.
            const onTouchEnd = (e: TouchEvent) => {
                if (this.isTouching && e.touches.length === 0)
                    this.onTouchEnd();
            };
            document.addEventListener('touchend', onTouchEnd, opts);
            document.addEventListener('touchcancel', onTouchEnd, opts);
            // A resize (e.g. mobile keyboard hiding, or a sub-header opening/closing) changes limits without
            // firing a scroll event. It re-pins authoritatively, so any boundary it crosses isn't a user
            // gesture: suppress the rubber-band spring (and cancel one in flight) and snap to the new limits
            // instead of animating a return — otherwise a viewport growth springs the edge back over ~400ms.
            this.resizeObserver = new ResizeObserver(() => {
                this.suppressUntil = Date.now() + ProgrammaticScrollSuppressMs;
                if (this.isReturning)
                    this.finishOverscrollReturn();
                this.clampToLimits();
            });
            this.resizeObserver.observe(element);
        }
        ScrollController.all.add(this);
        (element as unknown as { scrollController?: ScrollController }).scrollController = this;
    }

    public dispose(): void {
        this.finishOverscrollReturn();
        ++this.touchLoopEpoch;
        this.resizeObserver?.disconnect();
        this.abort.abort();
        ScrollController.all.delete(this);
    }

    public static cancelMomentumAll(): void {
        for (const controller of this.all)
            controller.cancelMomentum();
    }

    public scrollTo(target: number | HTMLElement, options: ScrollToOptions = {}): void {
        this.suppressUntil = Date.now() + ProgrammaticScrollSuppressMs;
        if (typeof target === 'number') {
            let top = target + (options.offset ?? 0);
            if (options.clamp !== false) {
                const limits = this.getEffectiveScrollLimits();
                top = clamp(top, limits.min, limits.max);
            }
            // An authoritative scroll to a new position supersedes a settling overscroll bounce; without this
            // the spring's per-frame boundary write keeps yanking scrollTop back to its now-stale edge (e.g.
            // the content grew and we re-pinned past it), stranding the list above the edge.
            if (this.isReturning && Math.abs(top - this.overscrollBoundary) > FixPrecisionPx)
                this.finishOverscrollReturn();
            if (options.smooth === false) {
                this.element.scrollTop = top;
                void this.element.offsetHeight; // Triggers reflow
                this.nudgeRepaint();
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

    public clampToLimits(): void {
        if (!this.enableScrollConstraints || this.isTouching || this.isReturning)
            return;

        const limits = this.getEffectiveScrollLimits();
        const clamped = clamp(this.element.scrollTop, limits.min, limits.max);
        if (Math.abs(this.element.scrollTop - clamped) > FixPrecisionPx)
            this.scrollTo(clamped, { smooth: false });
    }

    public getEffectiveScrollLimits(): { min: number, max: number } {
        // null edge => no limit that way (free scroll): ±Infinity, so the rubber-band and clamp never engage.
        const raw = this.getScrollLimits();
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

        const boundary = this.getViolatedBoundary(scrollTop);
        if (boundary === null) {
            if (this.overscrollSign !== 0) {
                this.overscrollSign = 0;
                this.clearTransform();
            }
            return;
        }
        const over = scrollTop - boundary;
        this.overscrollSign = Math.sign(over);
        this.startOverscrollReturn(boundary, this.overscrollSign * flingEntryOffset(Math.abs(over)), this.recentSpeed);
    }

    private onTouchStart(): void {
        this.isTouching = true;
        if (this.isReturning)
            this.finishOverscrollReturn();
        this.startTouchLoop();
    }

    private onTouchEnd(): void {
        this.isTouching = false;
        const scrollTop = this.element.scrollTop;
        const boundary = this.getViolatedBoundary(scrollTop);
        if (boundary !== null) {
            this.overscrollSign = Math.sign(scrollTop - boundary);
            this.startOverscrollReturn(boundary, this.overscrollSign * visibleOverscroll(Math.abs(scrollTop - boundary)), this.touchSpeed);
        }
        else if (this.overscrollSign !== 0) {
            this.overscrollSign = 0;
            this.clearTransform();
        }
    }

    private getViolatedBoundary(scrollTop: number): number | null {
        const limits = this.getEffectiveScrollLimits();
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
                this.touchSpeed = 0.6 * speed + 0.4 * this.touchSpeed;
                this.touchPrevTop = scrollTop;
                this.touchPrevTime = now;
            }

            const limits = this.getEffectiveScrollLimits();
            const sign = scrollTop < limits.min ? -1 : scrollTop > limits.max ? 1 : 0;
            if (sign !== 0) {
                const boundary = sign < 0 ? limits.min : limits.max;
                this.overscrollSign = sign;
                this.applyTranslate(sign * resistancePull(Math.abs(scrollTop - boundary)));
            }
            else if (this.overscrollSign !== 0) {
                this.overscrollSign = 0;
                this.clearTransform();
            }
            fastRaf({ write: tick });
        };
        fastRaf({ write: tick });
    }

    private startOverscrollReturn(boundary: number, offset: number, speedPxMs: number): void {
        this.overscrollBoundary = boundary;
        this.springOffset = offset;
        this.springCap = Math.max(Math.abs(offset), MaxFlingOverscrollPx);
        this.springSpeed = clamp(speedPxMs * 1000, -MaxReturnSpeedPxS, MaxReturnSpeedPxS);
        // Non-Safari: hold overflow:hidden for the whole spring — flicker-free, and these browsers still
        // re-attach a scroll to a mid-bounce touch.
        if (!DeviceInfo.isWebKit)
            this.setOverflowLocked(true);
        this.element.scrollTop = boundary;
        this.applyTranslate(-this.springOffset);
        if (this.isReturning)
            return;

        this.isReturning = true;
        // Safari won't scroll an element that was overflow:hidden when the finger landed, so a held lock
        // would block interrupting the bounce. Lock one frame (kills the fling), then stay scrollable.
        if (DeviceInfo.isWebKit) {
            this.setOverflowLocked(true);
            void this.element.offsetHeight;
            fastRaf({ write: () => { if (this.isReturning) this.setOverflowLocked(false); } });
        }
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
                this.finishOverscrollReturn();
                return;
            }
            if (Math.abs(this.element.scrollTop - this.overscrollBoundary) > FixPrecisionPx)
                this.element.scrollTop = this.overscrollBoundary;
            this.applyTranslate(-this.springOffset);
            fastRaf({ write: tick });
        };
        fastRaf({ write: tick });
    }

    private finishOverscrollReturn(): void {
        this.isReturning = false;
        this.springOffset = 0;
        this.springSpeed = 0;
        this.overscrollSign = 0;
        ++this.returnEpoch;
        this.clearTransform();
        this.setOverflowLocked(false);
    }

    private cancelMomentum(): void {
        if (!this.enableScrollConstraints || this.isReturning || this.isTouching)
            return;

        this.setOverflowLocked(true);
        void this.element.offsetHeight;
        fastRaf({ write: () => { if (!this.isReturning) this.setOverflowLocked(false); } });
    }

    // WebKit can leave a paint-contained, composited scroller unrastered after a programmatic scrollTop
    // write: on iOS the chat view lands on the new position showing nothing, and only the next touch
    // brings it back. The offsetHeight read above forces layout, not paint, so it can't help - invalidating
    // the layer can. The rubber-band owns this same property, so this stays out of its way in both
    // directions: it doesn't start one while the band is active, and it only clears a value it still owns.
    private nudgeRepaint(): void {
        if (!DeviceInfo.isWebKit || this.isTouching || this.isReturning)
            return;

        const element = this.overscrollElement;
        element.style.transform = RepaintNudgeTransform;
        fastRaf({
            write: () => {
                if (element.style.transform === RepaintNudgeTransform)
                    this.clearTransform();
            },
        });
    }

    private applyTranslate(y: number): void {
        this.overscrollElement.style.transform = `translate3d(0, ${y}px, 0)`;
    }

    private clearTransform(): void {
        this.overscrollElement.style.transform = '';
    }

    private setOverflowLocked(locked: boolean): void {
        if (this.isOverflowLocked === locked)
            return;

        this.isOverflowLocked = locked;
        this.element.style.overflowY = locked ? 'hidden' : '';
    }
}

// Helpers

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

// Maps a one-frame fling overshoot through an asymptote so the spring starts from a bounded offset.
function flingEntryOffset(over: number): number {
    return (over * FlingEntryAsymptotePx) / (over + FlingEntryAsymptotePx);
}

(globalThis as unknown as { ScrollController: typeof ScrollController }).ScrollController = ScrollController;

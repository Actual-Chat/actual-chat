import { clamp } from 'math';
import { fastRaf } from 'fast-raf';
import { DeviceInfo } from 'device-info';

export interface ScrollLimits {
    min: number | null;
    max: number | null;
}

export interface ScrollToOptions {
    smooth?: boolean;
    clamp?: boolean;
    offset?: number;
    // This write moves the coordinate system, not the view: the caller re-anchored its content and is
    // paying for it so nothing appears to move. A return in flight is carried across it rather than
    // cancelled - see scrollTo.
    reanchor?: boolean;
}

const FixPrecisionPx = 0.1;
const ProgrammaticScrollSuppressMs = 300;
// Sub-pixel, so it changes the layer's transform matrix without moving anything you can see.
const RepaintNudgePx = 0.01;

// Touch rubber-band: resistance ramps 0 -> MaxResistance over ResistanceRampPx of pull (soft, spring-like).
const MaxResistance = 0.667;
const ResistanceRampPx = 667;

// Release/fling return spring: critically damped (single excursion, no second bounce).
// Two stiffnesses, because the spring is asked to do two different jobs. Letting go of a held pull
// wants the snappy one: the content is at rest, and the whole motion is the return, which reads as
// sluggish and hard to follow when it is drawn out. Arriving at the edge with a fling wants the soft
// one: the damping force is proportional to the incoming speed, and at 120 it took ~2200px/s out of a
// fast spin in a single frame, which is a stop, not a brake. Entry speed picks between them.
const ReturnStiffness = 120;
const FlingReturnStiffness = 70;
// Above this the return is braking real momentum rather than releasing a static pull.
const FlingEntrySpeedPxMs = 0.5;
const ReturnDampingRatio = 1;
const MaxReturnSpeedPxS = 3000;
const ReturnSettlePx = 0.3;
const ReturnSettleSpeedPxS = 8;
const MaxFlingOverscrollPx = 220;
// A finger that hasn't moved the scroller for this long isn't a finger: it's a touchend that
// never arrived, and the flag it left behind disarms every recovery path.
const TouchStaleMs = 3000;
const FlingEntryAsymptotePx = 100;
// Past this much runaway the transform stops hiding the native scroll and it is written back instead:
// the list reads scrollTop to decide what to load, and it should not see a position nobody is at.
const MaxDriftCompensationPx = 440;

// Keeps an element's scrollTop within getScrollLimits()'s [min, max] band with iOS-style rubber-band
// overscroll: finger-down past the edge tracks with ramping resistance (scroll not blocked); no-finger
// past the edge ends the momentum once and a critically-damped spring returns it, carrying the whole
// displacement in the element's transform so nothing has to write scrollTop while it runs.
export class ScrollController {
    private static readonly all = new Set<ScrollController>();

    private readonly abort = new AbortController();

    private isTouching = false;
    private lastScrollTop = 0;
    private lastScrollTime = 0;
    private recentSpeed = 0;            // px/ms, signed, smoothed (no-touch path)
    private suppressUntil = 0;
    private isOverflowLocked = false;
    private overflowLockEpoch = 0;
    private resizeObserver: ResizeObserver | null = null;

    // Composed into the one transform, since only one writer of a property can win and these three
    // come and go independently of each other.
    private overscrollOffset = 0;       // px, the rubber band's own displacement
    private baseOffset = 0;             // px, the owner's, via setBaseOffset
    private hasRepaintNudge = false;

    private overscrollSign = 0;         // +1 = past max (bottom), -1 = past min (top), 0 = in band
    private overscrollBoundary = 0;

    private touchLoopEpoch = 0;
    private isTouchLooping = false;
    private touchPrevTop = 0;
    private touchPrevTime = 0;
    private touchSpeed = 0;             // px/ms, smoothed
    private touchBoundary: number | null = null;  // px, the edge this pull is measured from
    private touchStillTop = 0;          // px, last position the touch loop saw move
    private touchStillSince = 0;

    private isReturning = false;
    private returnEpoch = 0;
    private springOffset = 0;           // px, signed visible overscroll
    private springSpeed = 0;            // px/s
    private springCap = 0;              // px, max |springOffset| (graceful clamp)
    private springStiffness = ReturnStiffness;

    // A finger is down: a scrollTop write would land under the gesture and end its momentum.
    public get isTouchActive(): boolean {
        return this.isTouching;
    }
    // Past a boundary right now - held there by a finger, or on the way back. Either way the content
    // is carrying a transform, so a caller that redefines the coordinates or clears the overscroll has
    // a step of up to the whole pull to pay for.
    // The position is tested directly rather than read off overscrollSign, because the touch loop only
    // latches that on its next frame: for the frame in between, a finger already past the edge would
    // report nothing, and a caller acting on that reading takes the pull for itself.
    public get isOverscrollActive(): boolean {
        return this.isReturning
            || this.overscrollSign !== 0
            || (this.isTouching && this.getViolatedBoundary(this.element.scrollTop) !== null);
    }

    constructor(
        private readonly element: HTMLElement,
        private readonly enableScrollConstraints = false,
        private readonly overscrollElement: HTMLElement = element,
        private readonly getScrollLimits: () => ScrollLimits = () => ({ min: null, max: null }),
    ) {
        if (this.enableScrollConstraints) {
            this.lastScrollTop = element.scrollTop;
            this.lastScrollTime = performance.now();
            const opts = { passive: true, signal: this.abort.signal };
            element.addEventListener('scroll', () => this.onScroll(), opts);
            element.addEventListener('touchstart', () => this.onTouchStart(), opts);
            // On the document, not the element: a touch keeps the target it started on, and a virtualized
            // list unloads items out from under the finger - so a gesture that began on a row that is gone
            // by the time it ends delivers touchend to a detached node. The element would never hear it,
            // isTouching would latch on, and with it every clamp and the return spring stay disarmed.
            const onTouchEnd = (e: TouchEvent) => { if (e.touches.length === 0) this.onTouchEnd(); };
            const endOpts = { passive: true, capture: true, signal: this.abort.signal };
            document.addEventListener('touchend', onTouchEnd, endOpts);
            document.addEventListener('touchcancel', onTouchEnd, endOpts);
            // A resize (e.g. mobile keyboard hiding, or a sub-header opening/closing) changes limits without
            // firing a scroll event. It re-pins authoritatively, so any boundary it crosses isn't a user
            // gesture: suppress the rubber-band spring (and cancel one in flight) and snap to the new limits
            // instead of animating a return — otherwise a viewport growth springs the edge back over ~400ms.
            this.resizeObserver = new ResizeObserver(() => {
                this.suppressUntil = performance.now() + ProgrammaticScrollSuppressMs;
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
        this.suppressUntil = performance.now() + ProgrammaticScrollSuppressMs;
        if (typeof target === 'number') {
            let top = target + (options.offset ?? 0);
            if (options.clamp !== false) {
                const limits = this.getEffectiveScrollLimits();
                top = clamp(top, limits.min, limits.max);
            }
            // An authoritative scroll to a new position supersedes a settling overscroll bounce; without this
            // the spring's per-frame boundary write keeps yanking scrollTop back to its now-stale edge (e.g.
            // the content grew and we re-pinned past it), stranding the list above the edge.
            // A re-anchor is not that. It is the same view, renumbered - the list moved its content in
            // wrapper coordinates and is writing the offset that cancels it out. Overscrolling an edge is
            // exactly what makes the list load the data that then re-anchors, so treating it as a new
            // destination kills nearly every spring a second after it starts: the finger lifts, the bounce
            // begins, the data lands, and the content snaps to where the bounce was heading. The boundary
            // is a coordinate too, so it moves by the same delta and the spring carries on undisturbed.
            if (this.isReturning) {
                if (options.reanchor)
                    this.overscrollBoundary += top - this.element.scrollTop;
                else if (Math.abs(top - this.overscrollBoundary) > FixPrecisionPx)
                    this.finishOverscrollReturn();
            }
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

    // Always a snap, never a spring. Handing an out-of-band position to startOverscrollReturn looks
    // like the gentler option, and on WebKit it would be - but on every other engine that call locks
    // overflow for the whole return, and locking overflow mid-fling ends the fling on the spot. Since
    // applyLayout clamps on every render, that turned any transient out-of-band position during a spin
    // into a dead stop at a random place. A snap is worse in theory and much better in practice.
    // Settles a return in flight. Its boundary belongs to the axis it started on, so anything about to
    // replace that axis has to call this first - otherwise every remaining frame writes a position that
    // means nothing in the new one.
    public cancelOverscroll(): void {
        if (this.isReturning)
            this.finishOverscrollReturn();
    }

    // Forgets the motion the position implies, without claiming the list has stopped. Speed here is
    // always a difference of two scrollTops, so a caller that redefines what scrollTop means - flipping
    // the render direction moves the origin by the whole scroll space - hands the estimators a step of
    // millions of pixels. Left alone that is the speed a release then springs back with.
    public resetMotionTracking(): void {
        const now = performance.now();
        const scrollTop = this.element.scrollTop;
        this.lastScrollTop = scrollTop;
        this.lastScrollTime = now;
        this.recentSpeed = 0;
        this.touchPrevTop = scrollTop;
        this.touchPrevTime = now;
        this.touchSpeed = 0;
        this.touchStillTop = scrollTop;
        this.touchStillSince = now;
    }

    public clampToLimits(): void {
        if (!this.enableScrollConstraints || this.isTouching || this.isReturning)
            return;

        const limits = this.getEffectiveScrollLimits();
        const clamped = clamp(this.element.scrollTop, limits.min, limits.max);
        if (Math.abs(this.element.scrollTop - clamped) > FixPrecisionPx)
            this.scrollTo(clamped, { smooth: false });
    }

    // The owner's own translation of the overscroll element, on top of whatever the rubber band is
    // doing to it. It is how a caller moves content without writing scrollTop - a transform composites
    // after layout, so unlike a scroll write it can't end a fling - and the two offsets are simply
    // added, since they displace the same element for unrelated reasons.
    public setBaseOffset(offset: number): void {
        if (this.baseOffset === offset)
            return;

        this.baseOffset = offset;
        this.writeTransform();
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
        const now = performance.now();
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
        this.touchBoundary = null;
        if (this.isReturning)
            this.catchOverscrollReturn();
        this.startTouchLoop();
    }

    // A finger landing on a bounce takes it over where it is; it does not send it home first. The two
    // phases express the same displacement differently - the spring holds the scroller at the boundary
    // and carries the offset in the transform, while a drag lets the scroller run past and keeps only
    // the resisted remainder - so the handover is a change of representation, not of position. Ending
    // the return instead would drop up to MaxFlingOverscrollPx of offset in the frame the finger lands.
    private catchOverscrollReturn(): void {
        const sign = Math.sign(this.springOffset);
        const visible = Math.abs(this.springOffset);
        if (sign === 0 || visible <= ReturnSettlePx) {
            this.finishOverscrollReturn();
            return;
        }

        const boundary = this.overscrollBoundary;
        const over = overscrollFromVisible(visible);
        this.isReturning = false;
        this.springOffset = 0;
        this.springSpeed = 0;
        ++this.returnEpoch;
        this.setOverflowLocked(false);
        this.overscrollSign = sign;
        this.element.scrollTop = boundary + sign * over;
        this.applyTranslate(sign * resistancePull(over));
    }

    private onTouchEnd(): void {
        // The listener is on the document, so this fires for gestures that never touched this list.
        if (!this.isTouching)
            return;

        this.isTouching = false;
        const scrollTop = this.element.scrollTop;
        const latched = this.touchBoundary;
        this.touchBoundary = null;
        const boundary = this.getViolatedBoundary(scrollTop);
        if (boundary !== null) {
            this.overscrollSign = Math.sign(scrollTop - boundary);
            const over = Math.abs(scrollTop - boundary);
            this.startOverscrollReturn(
                boundary, this.overscrollSign * visibleOverscroll(over), this.touchSpeed);
        }
        else if (this.overscrollSign !== 0 && latched != null) {
            // The finger never came back - the edge moved out from under it while it held. The position
            // is legal now, but the transform is still holding a pull's worth of displacement, and
            // dropping it would be a step of exactly that. Springing it away instead is the same motion
            // every other release produces; the boundary is the position itself, so nothing scrolls.
            const pull = resistancePull(Math.abs(scrollTop - latched));
            this.startOverscrollReturn(scrollTop, -this.overscrollSign * pull, 0);
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
        this.touchPrevTime = performance.now();
        this.touchStillTop = this.element.scrollTop;
        this.touchStillSince = this.touchPrevTime;
        this.touchSpeed = 0;
        const epoch = ++this.touchLoopEpoch;
        const tick = (time: number) => {
            if (epoch !== this.touchLoopEpoch || !this.isTouching) {
                this.isTouchLooping = false;
                return;
            }

            const scrollTop = this.element.scrollTop;
            const dt = time - this.touchPrevTime;
            if (Math.abs(scrollTop - this.touchStillTop) > FixPrecisionPx) {
                this.touchStillTop = scrollTop;
                this.touchStillSince = time;
            }
            else if (time - this.touchStillSince > TouchStaleMs) {
                // Nothing has moved for this long, so whatever we are waiting on is not a gesture: the
                // touchend went to a node the list had already unloaded, or the browser dropped it. Left
                // alone the flag never clears, and with it the clamp and the return spring that are the
                // only way back from an overscroll - the list stays parked off its own content.
                this.onTouchEnd();
                this.isTouchLooping = false;
                return;
            }

            if (dt > 0) {
                const speed = (scrollTop - this.touchPrevTop) / dt;
                this.touchSpeed = 0.6 * speed + 0.4 * this.touchSpeed;
                this.touchPrevTop = scrollTop;
                this.touchPrevTime = time;
            }

            // The edge is captured when the pull starts and held until the finger comes back inside it.
            // Read fresh each frame instead, a page of history arriving mid-pull moves the limit and the
            // resistance jumps with it - the content steps under a finger that never moved. The pull is
            // a gesture, and it is measured from where the gesture began.
            let sign = this.overscrollSign;
            let boundary = this.touchBoundary;
            if (sign === 0 || boundary == null) {
                const limits = this.getEffectiveScrollLimits();
                sign = scrollTop < limits.min ? -1 : scrollTop > limits.max ? 1 : 0;
                boundary = sign < 0 ? limits.min : sign > 0 ? limits.max : null;
            }
            else if (sign < 0 ? scrollTop >= boundary : scrollTop <= boundary) {
                sign = 0;
                boundary = null;
            }

            if (sign !== 0 && boundary != null) {
                this.overscrollSign = sign;
                this.touchBoundary = boundary;
                this.applyTranslate(sign * resistancePull(Math.abs(scrollTop - boundary)));
            }
            else if (this.overscrollSign !== 0) {
                // Back inside the edge it started from, where the pull is zero anyway.
                this.overscrollSign = 0;
                this.touchBoundary = null;
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
        this.springStiffness = Math.abs(speedPxMs) > FlingEntrySpeedPxMs
            ? FlingReturnStiffness
            : ReturnStiffness;
        this.element.scrollTop = boundary;
        this.applyTranslate(-this.springOffset);
        if (this.isReturning)
            return;

        this.isReturning = true;
        // The momentum that carried the view past the edge is ended once, here, which is what a native
        // rubber band does - and from there the spring owns the visible position through the transform
        // alone. Held locked for the whole return instead, as every non-WebKit engine used to be, the
        // scroller is dead for the ~300ms the spring runs and something has to write scrollTop back
        // each frame; one missed frame in that stream is the jitter. WebKit additionally refuses to
        // scroll an element that was overflow:hidden when the finger landed, so a held lock would also
        // block catching the bounce.
        this.killMomentum();
        const epoch = ++this.returnEpoch;
        // Integrated on the frame clock, and seeded by the first frame rather than by the release that
        // scheduled it: the wait in between is input-to-frame latency, not a step of the motion, and
        // charging it to the spring makes the bounce start already part-way through.
        let lastTime: number | null = null;
        const tick = (time: number) => {
            if (epoch !== this.returnEpoch)
                return;

            const dt = lastTime === null ? 0 : Math.min((time - lastTime) / 1000, 1 / 30);
            lastTime = time;
            if (dt > 0) {
                const k = this.springStiffness;
                const c = ReturnDampingRatio * 2 * Math.sqrt(k);
                this.springSpeed += (-k * this.springOffset - c * this.springSpeed) * dt;
                // Clamped, and the outward speed dropped with it: left in, it presses against the cap
                // until damping bleeds it off, which measured as ~115ms of dead time at the start of
                // every release - the bounce looks frozen before it moves.
                let next = this.springOffset + this.springSpeed * dt;
                if (next > this.springCap) {
                    next = this.springCap;
                    if (this.springSpeed > 0)
                        this.springSpeed = 0;
                }
                else if (next < -this.springCap) {
                    next = -this.springCap;
                    if (this.springSpeed < 0)
                        this.springSpeed = 0;
                }
                this.springOffset = next;
            }
            if (Math.abs(this.springOffset) <= ReturnSettlePx && Math.abs(this.springSpeed) <= ReturnSettleSpeedPxS) {
                this.finishOverscrollReturn();
                return;
            }

            // The lock lasted one frame, so the scroller is live for the rest of the spring and a
            // scrollTop write per frame would fight anything that survived it. The native scroll is
            // left to run instead and the transform carries the whole effect, compensating whatever it
            // has done since the boundary. Only a drift too large to hide that way is written back -
            // and that write needs the momentum behind it gone, or the next frame undoes it.
            const drift = this.element.scrollTop - this.overscrollBoundary;
            const mustPin = Math.abs(drift) > MaxDriftCompensationPx;
            if (mustPin) {
                this.element.scrollTop = this.overscrollBoundary;
                this.killMomentum();
            }
            this.applyTranslate(mustPin ? -this.springOffset : -this.springOffset + drift);
            fastRaf({ write: tick });
        };
        fastRaf({ write: tick });
    }

    private finishOverscrollReturn(): void {
        const wasReturning = this.isReturning;
        this.isReturning = false;
        this.springOffset = 0;
        this.springSpeed = 0;
        this.overscrollSign = 0;
        ++this.returnEpoch;
        // Where the compensated path settles up: the native scroll ran on while the transform hid it,
        // so the two are reconciled here - one write, in the frame the transform is dropped.
        if (wasReturning && Math.abs(this.element.scrollTop - this.overscrollBoundary) > FixPrecisionPx)
            this.element.scrollTop = this.overscrollBoundary;
        this.clearTransform();
        this.setOverflowLocked(false);
    }

    private cancelMomentum(): void {
        if (!this.enableScrollConstraints || this.isReturning || this.isTouching)
            return;

        this.killMomentum();
    }

    // One frame of overflow:hidden, which is what it takes to end momentum: every engine drops the
    // fling of a scroller that stops being scrollable, and a single frame is short enough that the
    // element is scrollable again before the user can ask it to move.
    private killMomentum(): void {
        const epoch = ++this.overflowLockEpoch;
        this.setOverflowLocked(true);
        // The lock has to land in this frame, not in the one the unlock is already scheduled for.
        void this.element.offsetHeight;
        fastRaf({ write: () => { if (epoch === this.overflowLockEpoch) this.setOverflowLocked(false); } });
    }

    // WebKit can leave a paint-contained, composited scroller unrastered after a programmatic scrollTop
    // write: on iOS the chat view lands on the new position showing nothing, and only the next touch
    // brings it back. The offsetHeight read above forces layout, not paint, so it can't help - invalidating
    // the layer can. The rubber-band owns this same property, so this stays out of its way in both
    // directions: it doesn't start one while the band is active, and it only clears a value it still owns.
    private nudgeRepaint(): void {
        if (!DeviceInfo.isWebKit || this.isTouching || this.isReturning)
            return;

        this.hasRepaintNudge = true;
        this.writeTransform();
        fastRaf({
            write: () => {
                if (!this.hasRepaintNudge)
                    return;

                this.hasRepaintNudge = false;
                this.writeTransform();
            },
        });
    }

    private clearTransform(): void {
        this.applyTranslate(0);
    }

    private applyTranslate(y: number): void {
        if (this.overscrollOffset === y)
            return;

        this.overscrollOffset = y;
        this.writeTransform();
    }

    // The only writer of the property, so that none of its three contributors can erase the others.
    private writeTransform(): void {
        const y = this.overscrollOffset + this.baseOffset + (this.hasRepaintNudge ? RepaintNudgePx : 0);
        const transform = y === 0 ? '' : `translate3d(0, ${y}px, 0)`;
        if (this.overscrollElement.style.transform !== transform)
            this.overscrollElement.style.transform = transform;
    }

    private setOverflowLocked(locked: boolean): void {
        if (this.isOverflowLocked === locked)
            return;

        this.isOverflowLocked = locked;
        this.element.style.overflowY = locked ? 'hidden' : '';
    }
}

// The overscroll a finger must be holding for `visible` px to be on screen - the inverse of
// visibleOverscroll, needed when a drag takes over from a spring that measures the same displacement
// the other way round.
function overscrollFromVisible(visible: number): number {
    const rampVisible = ResistanceRampPx * (1 - MaxResistance / 2);
    if (visible >= rampVisible)
        return (visible - (MaxResistance * ResistanceRampPx) / 2) / (1 - MaxResistance);

    const k = MaxResistance / (2 * ResistanceRampPx);
    return (1 - Math.sqrt(Math.max(0, 1 - 4 * k * visible))) / (2 * k);
}

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

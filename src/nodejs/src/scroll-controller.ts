import { clamp } from 'math';
import { fastRaf } from 'fast-raf';
import { DeviceInfo } from 'device-info';

export interface ScrollLimits {
    min: number | null;
    max: number | null;
}

// Diagnostics only; nothing reads this to drive behaviour.
export interface ScrollDebugState {
    phase: string;
    visible: number;
    drift: number;
    offset: number;
    locked: boolean;
    speed: number;
    scrollSpeed: number;
    springVisible: number;
    decision: string;
}

export interface ScrollToOptions {
    smooth?: boolean;
    clamp?: boolean;
    offset?: number;
    // A renumbering, not a destination: the boundary moves by the same delta and a return in flight
    // carries on - see scrollTo.
    reanchor?: boolean;
}

const FixPrecisionPx = 0.1;
const ProgrammaticScrollSuppressMs = 300;
// Sub-pixel: changes the transform matrix without moving anything visible.
const RepaintNudgePx = 0.01;

// Resistance ramps 0 -> MaxResistance over ResistanceRampPx of pull. Gentle on purpose: the transform
// carries exactly what the curve eats, and on an off-main-thread scroller any disagreement between the
// composited position and the one read here reaches the screen scaled by the curve's slope. Measured:
// this curve puts 5px in the transform at 100px of pull, a UIScrollView-shaped one 48px, and shook ~10x
// harder for it. Overridable as ?vlfriction=<max>x<ramp> for tuning on a device.
const [MaxResistance, ResistanceRampPx] = readFriction(0.667, 444);

// The return floor at displacement x is the speed a critically damped spring of stiffness k, released
// from rest 20% further out, has reached by the time it passes x (from rest the speed at x itself is
// zero). x(t) = x0 (1 + wt) e^(-wt), w = sqrt(k); from x0 = 1.2x it passes x at wt = 0.731, moving at
// ReturnFloorFactor * w * x. See tickSpring.
const ReturnStiffness = 1600;
const ReturnFloorFactor = 0.4223;
const ReturnSettlePx = 0.3;
const MaxReturnSpeedPxS = 6000;
// A release still heading out keeps its momentum: the display carries it under the same spring until
// it turns, and the floor takes over from there. A bounce goes at most MaxBouncePx beyond where it was
// released - the browser's fling is being ended meanwhile, and on an engine where nothing ends it
// (nolock) the display would otherwise follow the whole fling. From the edge a critically damped spring
// with initial speed v peaks at v / (w e), so the same number bounds the carried speed.
const MaxBouncePx = 150;
const MaxCarrySpeedPxS = MaxBouncePx * Math.sqrt(ReturnStiffness) * Math.E;
// A crossing smaller than this is a rounding error and is snapped away; taking the scroller over for
// one is what makes an edge feel sticky.
const MinExcursionPx = 2;
// A finger still for this long is a touchend that never arrived.
const TouchStaleMs = 3000;
// A gap this long between scroll events ends the motion; the next one says for itself what it is.
const MotionGapMs = 200;
// Backstop, not mechanism: if a phase ever stops advancing, this hands the element back. Nothing else
// can, because everything else runs on the frame loop that failed.
const LockWatchdogMs = 1500;
// Stable frames of overflow:hidden used to confirm that the native fling has stopped.
const MomentumKillFrames = 2;
const MomentumSampleWindowMs = 96;
const MomentumSampleCapacity = 12;

// These phases describe the rubber band; MomentumPhase separately describes who advances the position.
type Phase =
    | 'in-band'
    // Past an edge with a finger on it: the band is drawn over whatever the gesture does.
    | 'following'
    // Past an edge with nobody holding it: the band is drawn over whatever the scroll does, and the
    // return floor adds only what that motion falls short of.
    | 'engaged';

type MomentumPhase = 'none' | 'arming' | 'transform';

interface MotionSample {
    readonly top: number;
    readonly time: number;
}

// Keeps an element's scrollTop within getScrollLimits()'s [min, max] band with an iOS-style rubber
// band drawn in the element's transform. The position the user sees is the pair (scrollTop, transform).
// A WebKit release atomically trades the first term for the second and returns entirely in the transform;
// a caught return trades it back. Every scrollTop write is read back. A wheel stops at the edge.
export class ScrollController {
    private static readonly all = new Set<ScrollController>();

    private readonly abort = new AbortController();

    // Fires whenever the composed transform changes - see InfiniteList's sticky items.
    public onTransform: (() => void) | null = null;

    private isTouching = false;
    private lastScrollTop = 0;
    private lastScrollTime = 0;
    private recentSpeed = 0;            // px/ms, signed, smoothed - the native scroll's own speed
    private suppressUntil = 0;
    private isOverflowLocked = false;
    private overflowLockEpoch = 0;
    private resizeObserver: ResizeObserver | null = null;

    // Composed into one transform; the three come and go independently.
    private overscrollOffset = 0;       // px, the rubber band's own displacement
    private baseOffset = 0;             // px, the owner's, via setBaseOffset
    private hasRepaintNudge = false;

    private phase: Phase = 'in-band';
    private isLooping = false;
    // The band's state. `boundary` is the edge in scrollTop px, latched at the crossing. `over` is the
    // raw pull the display corresponds to - the invariant is that what is on screen past the edge is
    // exactly signedOverscroll(over) at every frame boundary. It is fed by the scroll's deltas and
    // reduced by the return through the curve's inverse; it is not the raw scroll position, and nothing
    // here needs that.
    private boundary = 0;
    private over = 0;
    // px/s, signed like `over`: the display's own outward speed after a release, while it has any -
    // and how far out (unsigned, in display px) it may carry the content.
    private carried = 0;
    private bounceCap = 0;
    private lastBandTop = 0;
    private springLastTime = 0;
    private scrollSpeed = 0;            // px/ms, signed - what the return phase last measured
    // Only touch gets a band; a wheel, autoscroll, the keyboard and programmatic scrolls stop at the edge.
    private isTouchMotion = false;

    private lockWatchdog: ReturnType<typeof setTimeout> | null = null;

    private followTop = 0;
    private followTime = 0;
    private followSpeed = 0;            // px/ms, signed
    private stillTop = 0;
    private stillSince = 0;

    private momentumPhase: MomentumPhase = 'none';
    private momentumSamples = new Array<MotionSample>();
    private momentumVelocity = 0;
    private momentumSpringSide = 0;
    private momentumSpringVisible = 0;
    private momentumSpringVelocity = 0;

    public get isTouchActive(): boolean {
        return this.isTouching;
    }

    // The content is carrying a transform: redefining the coordinates now costs a step of up to the
    // whole displacement.
    public get isOverscrollActive(): boolean {
        return this.phase !== 'in-band' || this.momentumPhase !== 'none';
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
            element.addEventListener('wheel', () => this.onWheel(), opts);
            // On the document because a touch listener on this element costs WebKit a walk of its whole
            // subtree per rendering update (6-12% of its main thread, measured during a call), and
            // because a touch keeps the target it started on - a list that unloads rows from under the
            // finger delivers touchend to a node this element would never hear from.
            const docOpts = { passive: true, capture: true, signal: this.abort.signal };
            // A touch is dispatched to where it began, so containment sees exactly what the element saw.
            document.addEventListener('touchstart', (e: TouchEvent) => {
                if (e.target instanceof Node && element.contains(e.target))
                    this.onTouchStart();
            }, docOpts);
            // Only the last finger leaving is a release; onTouchEnd ignores a gesture this controller
            // never saw start, which every instance in the app now hears.
            const onTouchEnd = (e: TouchEvent) => {
                if (e.touches.length === 0)
                    this.onTouchEnd();
            };
            document.addEventListener('touchend', onTouchEnd, docOpts);
            document.addEventListener('touchcancel', onTouchEnd, docOpts);
            // A resize (keyboard, sub-header) moves the limits without a scroll event; snap rather than
            // spring, or a viewport growth bounces the edge back over ~400ms.
            this.resizeObserver = new ResizeObserver(() => {
                this.suppressUntil = performance.now() + ProgrammaticScrollSuppressMs;
                this.cancelOverscroll();
                this.clampToLimits();
            });
            this.resizeObserver.observe(element);
        }
        ScrollController.all.add(this);
        (element as unknown as { scrollController?: ScrollController }).scrollController = this;
    }

    public dispose(): void {
        this.stopMomentumTakeover();
        this.endPhase(null);
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
        if (!options.reanchor)
            this.stopMomentumTakeover();

        if (typeof target === 'number') {
            let top = target + (options.offset ?? 0);
            if (options.clamp !== false) {
                const limits = this.getEffectiveScrollLimits();
                top = clamp(top, limits.min, limits.max);
            }
            // Written before the band is re-aimed, so the re-aim can use what the engine accepted:
            // WebKit takes only part of a write while inertia runs, and a boundary taken from the
            // request would describe a position the content is not at. A smooth scroll lands over many
            // frames and has nothing to read back, so it re-aims at the request as before.
            const before = this.element.scrollTop;
            if (options.smooth === false) {
                this.element.scrollTop = top;
                void this.element.offsetHeight; // Triggers reflow
            }
            const landed = options.smooth === false ? this.element.scrollTop : top;
            // A re-anchor is the same view renumbered, so the boundary moves with it and a return in
            // flight carries on. Overscrolling an edge is what loads the data that re-anchors, so this
            // fires constantly. A new destination re-aims the excursion instead: boundary and `over`
            // move together so the screen does not, and the return carries the displacement off from
            // the new edge rather than dropping it.
            if (this.isOverscrollActive) {
                if (options.reanchor)
                    this.translateScrollCoordinates(landed - before);
                else if (Math.abs(landed - this.boundary) > FixPrecisionPx) {
                    this.over += this.boundary - landed;
                    this.boundary = landed;
                    if (this.phase !== 'engaged')
                        this.engage(0);
                }
            }
            if (options.smooth === false) {
                this.lastScrollTop = landed;
                this.lastScrollTime = performance.now();
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

    // A snap, never a spring: for callers about to redefine what scrollTop means.
    public cancelOverscroll(): void {
        this.stopMomentumTakeover();
        if (this.phase !== 'in-band')
            this.endPhase(this.boundary);
    }

    // For callers that redefine what scrollTop means: otherwise the estimators read the origin shift as
    // a step of millions of pixels.
    public resetMotionTracking(): void {
        this.lastScrollTop = this.element.scrollTop;
        this.lastScrollTime = performance.now();
        this.recentSpeed = 0;
        this.followSpeed = 0;
        this.momentumSamples = [];
    }

    public clampToLimits(): void {
        if (!this.enableScrollConstraints || this.isTouching || this.phase !== 'in-band')
            return;

        const limits = this.getEffectiveScrollLimits();
        const clamped = clamp(this.element.scrollTop, limits.min, limits.max);
        if (Math.abs(this.element.scrollTop - clamped) > FixPrecisionPx)
            this.scrollTo(clamped, { smooth: false });
    }

    // The owner's own translation, added to the band's; a transform can move content without ending a
    // fling, which a scrollTop write cannot.
    public setBaseOffset(offset: number): void {
        if (this.baseOffset === offset)
            return;

        this.baseOffset = offset;
        this.writeTransform();
    }

    public getDebugState(): ScrollDebugState {
        return {
            phase: this.phase,
            visible: this.phase === 'in-band' ? 0 : signedOverscroll(this.over),
            drift: this.phase === 'in-band' ? 0 : this.over,
            offset: this.overscrollOffset + this.baseOffset,
            locked: this.isOverflowLocked,
            speed: this.recentSpeed,
            scrollSpeed: this.phase === 'engaged' ? this.scrollSpeed : 0,
            springVisible: this.momentumSpringSide * this.momentumSpringVisible,
            decision: this.momentumPhase,
        };
    }

    // The band's part of the transform alone: a position derived from scrollTop is out by this much
    // for as long as an excursion lasts.
    public get bandOffset(): number {
        return this.overscrollOffset;
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
        if (this.phase !== 'engaged' && now >= this.suppressUntil && dt > 0 && dt < MotionGapMs) {
            const speed = (scrollTop - this.lastScrollTop) / dt;
            // Smoothed within a direction, replaced across one: blending through a reversal reads a
            // swing back into the list as slow and still outward for a frame.
            this.recentSpeed = speed * this.recentSpeed < 0
                ? speed
                : 0.6 * speed + 0.4 * this.recentSpeed;
        }
        else if (now < this.suppressUntil) {
            this.recentSpeed = 0;
        }

        // Without this the flag outlives its gesture, and a mouse inherits the band from a finger.
        if (dt >= MotionGapMs && !this.isTouching)
            this.isTouchMotion = false;
        if (this.isTouchMotion && this.momentumPhase === 'none' && now >= this.suppressUntil)
            this.addMomentumSample(scrollTop, now);

        this.lastScrollTop = scrollTop;
        this.lastScrollTime = now;
        if (this.phase === 'engaged' || this.momentumPhase !== 'none' || now < this.suppressUntil)
            return;

        const boundary = this.getViolatedBoundary(scrollTop);
        if (boundary === null) {
            // Back inside under its own power; `over` has been integrated to zero and the transform
            // with it, so there is nothing to hand back.
            if (this.phase === 'following')
                this.endPhase(null);

            return;
        }

        // Deliberately does not draw: the loop draws once per frame from one read, and a second write
        // from the scroll event is a second read of a position the compositor is moving - jitter.
        if (this.phase === 'following')
            return;

        const over = scrollTop - boundary;
        // Anything that is not a finger stops at the edge (middle-button autoscroll produces scroll
        // events and no wheel events, so it is caught here, not in onWheel).
        if (Math.abs(over) < MinExcursionPx || !(this.isTouching || this.isTouchMotion)) {
            this.element.scrollTop = boundary;
            // The snap is what the next event has to measure from: the tracking above recorded the
            // position before it, and leaving that reads the correction back as the user's own travel.
            this.lastScrollTop = this.element.scrollTop;
            this.lastScrollTime = performance.now();
            return;
        }

        // The boundary is latched: a page of history arriving mid-pull must not move the edge the pull is
        // measured from. Which side of it the content is on is just the sign of `over`.
        this.boundary = boundary;
        // Seeded at the true excursion, not integrated from the edge: a limit that moved under the
        // scroll is noticed thousands of pixels out, and treating that as one frame of finger travel
        // put 1480px into the transform in a single step (measured on Android). This is the one place
        // the band is seeded rather than nudged - there is no previous frame to be a delta from.
        this.over = scrollTop - boundary;
        this.lastBandTop = scrollTop;
        this.followTop = scrollTop;
        this.followTime = now;
        this.followSpeed = this.recentSpeed;
        this.stillTop = scrollTop;
        this.stillSince = now;
        this.nudge(this.over - signedOverscroll(this.over) - this.overscrollOffset);
        if (this.isTouching) {
            this.phase = 'following';
            this.startLoop();
        }
        else if (canTakeOverMomentum()) {
            this.phase = 'following';
            this.startMomentumTakeover();
        }
        else {
            this.engage(this.recentSpeed);
        }
    }

    // One frame of the scroll's motion through the resistance: the share the curve eats goes into the
    // transform, the rest reaches the screen. Integrated across the step, not sampled at its end.
    private follow(scrollTop: number): void {
        const delta = scrollTop - this.lastBandTop;
        this.lastBandTop = scrollTop;
        if (delta === 0)
            return;

        const before = signedOverscroll(this.over);
        this.over += delta;
        const after = signedOverscroll(this.over);
        this.nudge(delta - (after - before));
    }

    // `speedPxMs` is the raw scroll speed at the release; what the display carries is its outward
    // component through the curve's slope. An inward release carries nothing - that is a throw into
    // the list, and the browser performs it.
    private engage(speedPxMs: number): void {
        // A finger that crossed back inside and then lifted is still `following` here, because ending
        // that phase waits on a scroll event a release does not produce. It is a release, not a bounce.
        if (this.getViolatedBoundary(this.element.scrollTop) === null
            && Math.abs(this.overscrollOffset) <= FixPrecisionPx) {
            this.endPhase(null);
            return;
        }

        const speed = speedPxMs * 1000 * visibleSlope(Math.abs(this.over));
        this.carried = speed * Math.sign(this.over) > 0 ? speed : 0;
        this.bounceCap = Math.abs(signedOverscroll(this.over)) + MaxBouncePx;
        this.phase = 'engaged';
        this.armLockWatchdog();
        this.springLastTime = performance.now();
        this.startLoop();
    }

    private onTouchStart(): void {
        const now = performance.now();
        this.isTouching = true;
        this.isTouchMotion = true;
        this.stillSince = now;
        this.stopMomentumTakeover(true);
        this.addMomentumSample(this.element.scrollTop, now);
        if (this.phase !== 'engaged')
            return;

        // A catch. The pull continues from `over` as it stands; the scroll is written to match it, read
        // back, and the same delta comes out of the transform, so nothing on screen moves. A finger is
        // down and the list is still. The write pays down whatever the scroll leaked past the edge during
        // the return, which otherwise compounds across catches (measured: 82 -> 1194px over five).
        this.phase = 'following';
        this.carried = 0;
        this.setOverflowLocked(false);
        // Whatever the scroll did since the last frame is a delta like any other - integrated first, or
        // a fling still running under the finger shows that frame's worth unresisted.
        this.follow(this.element.scrollTop);
        const scrollTop = this.element.scrollTop;
        const wanted = this.boundary + this.over;
        if (Math.abs(scrollTop - wanted) > FixPrecisionPx) {
            this.element.scrollTop = wanted;
            const landed = this.element.scrollTop;
            this.nudge(landed - scrollTop);
            this.lastScrollTop = landed;
        }

        this.lastBandTop = this.element.scrollTop;
        this.followTop = this.element.scrollTop;
        this.followTime = performance.now();
        this.followSpeed = 0;
        this.stillTop = this.followTop;
        this.startLoop();
    }

    private onTouchEnd(): void {
        // The listener is on the document, so this fires for gestures that never touched this list.
        if (!this.isTouching)
            return;

        this.isTouching = false;
        if (this.phase !== 'following')
            return;

        if (canTakeOverMomentum())
            this.startMomentumTakeover();
        else
            this.engage(this.followSpeed);
    }

    private onWheel(): void {
        this.isTouchMotion = false;
        this.stopMomentumTakeover();
        if (this.phase !== 'in-band')
            this.endPhase(this.boundary);
    }

    private getViolatedBoundary(scrollTop: number): number | null {
        const limits = this.getEffectiveScrollLimits();
        if (scrollTop < limits.min - FixPrecisionPx)
            return limits.min;
        if (scrollTop > limits.max + FixPrecisionPx)
            return limits.max;

        return null;
    }

    // One loop for every phase, and the only thing that draws the band: once per frame from one read.
    // Events decide the phase and arm this; it stops itself when there is nothing left to draw.
    private startLoop(): void {
        if (this.isLooping)
            return;

        this.isLooping = true;
        let lastTime: number | null = null;
        const tick = (time: number) => {
            if (this.phase === 'in-band' && this.momentumPhase === 'none') {
                this.isLooping = false;
                return;
            }

            // Seeded by the first frame, not the event that scheduled it: that wait is latency, not motion.
            const dt = lastTime === null ? 0 : Math.min((time - lastTime) / 1000, 1 / 30);
            lastTime = time;
            this.armLockWatchdog();
            switch (this.momentumPhase) {
            case 'arming':
                this.tickMomentumArming();
                break;
            case 'transform':
                this.tickMomentumTransform(dt);
                break;
            case 'none':
                switch (this.phase) {
                case 'following':
                    this.tickFollow(time);
                    break;
                case 'engaged':
                    this.tickSpring(dt, time);
                    break;
                }
                break;
            }

            fastRaf({ write: tick });
        };
        fastRaf({ write: tick });
    }

    private startMomentumTakeover(): void {
        const now = performance.now();
        const scrollTop = this.element.scrollTop;
        if (this.phase !== 'in-band')
            this.follow(scrollTop);
        if (this.getViolatedBoundary(scrollTop) === null
            && Math.abs(this.overscrollOffset) <= FixPrecisionPx) {
            this.momentumSamples = [];
            this.endPhase(null);
            return;
        }

        this.addMomentumSample(scrollTop, now);
        this.carried = 0;
        this.momentumVelocity = this.estimateMomentumSpeed();
        this.scrollSpeed = this.momentumVelocity;
        this.momentumSamples = [];
        this.momentumPhase = 'arming';
        this.phase = 'engaged';
        this.armLockWatchdog();
        this.startLoop();
    }

    private tickMomentumArming(): void {
        const scrollTop = this.element.scrollTop;
        this.follow(scrollTop);
        const rawOver = this.over;
        const position = this.snapMomentumToBoundary(true);
        const screenVelocity = -this.momentumVelocity * 1000 * visibleSlope(Math.abs(rawOver));
        const side = Math.sign(position) || Math.sign(screenVelocity) || -Math.sign(rawOver);
        if (side === 0) {
            this.finishMomentumTransform();
            return;
        }

        this.momentumSpringSide = side;
        this.momentumSpringVisible = Math.abs(position);
        this.momentumSpringVelocity = clamp(
            side * screenVelocity,
            -MaxCarrySpeedPxS,
            MaxCarrySpeedPxS);
        this.bounceCap = this.momentumSpringVisible + MaxBouncePx;
        this.over = rawOverscroll(-position);
        this.momentumPhase = 'transform';
    }

    private translateScrollCoordinates(delta: number): void {
        if (delta === 0)
            return;

        this.boundary += delta;
        this.lastBandTop += delta;
        this.followTop += delta;
        this.stillTop += delta;
        this.lastScrollTop += delta;
        this.momentumSamples = this.momentumSamples.map(sample => ({
            top: sample.top + delta,
            time: sample.time,
        }));
    }

    private tickMomentumTransform(dt: number): void {
        if (Math.abs(this.element.scrollTop - this.boundary) > FixPrecisionPx) {
            const screenVelocity = this.momentumSpringSide * this.momentumSpringVelocity;
            const position = this.snapMomentumToBoundary(false);
            const side = Math.sign(position) || this.momentumSpringSide;
            this.momentumSpringSide = side;
            this.momentumSpringVisible = Math.abs(position);
            this.momentumSpringVelocity = side * screenVelocity;
            this.bounceCap = this.momentumSpringVisible + MaxBouncePx;
        }
        if (dt === 0)
            return;

        const x = this.momentumSpringVisible;
        const v = this.momentumSpringVelocity;
        let [nextX, nextV] = stepCriticalSpring(x, v, dt);
        if (nextX >= this.bounceCap) {
            nextX = this.bounceCap;
            nextV = Math.min(nextV, 0);
        }

        if (nextX <= 0 || (nextX <= ReturnSettlePx && nextV <= 0)) {
            this.applyMomentumPosition(0);
            this.finishMomentumTransform();
            return;
        }

        this.momentumSpringVisible = nextX;
        this.momentumSpringVelocity = nextV;
        this.applyMomentumPosition(this.momentumSpringSide * nextX);
    }

    private snapMomentumToBoundary(mustLock: boolean): number {
        const before = this.overscrollElement.getBoundingClientRect().top;
        if (mustLock) {
            this.setOverflowLocked(true, true);
            void this.element.offsetHeight;
        }
        this.element.scrollTop = this.boundary;
        void this.element.offsetHeight;
        const after = this.overscrollElement.getBoundingClientRect().top;
        this.nudge(before - after);

        const now = performance.now();
        const landed = this.element.scrollTop;
        this.lastBandTop = landed;
        this.lastScrollTop = landed;
        this.lastScrollTime = now;
        this.followTop = landed;
        this.followTime = now;
        this.stillTop = landed;
        return this.overscrollOffset - (landed - this.boundary);
    }

    private applyMomentumPosition(position: number): void {
        this.applyTranslate(position + this.element.scrollTop - this.boundary);
        this.over = rawOverscroll(-position);
    }

    private finishMomentumTransform(): void {
        if (Math.abs(this.element.scrollTop - this.boundary) > 1)
            return;

        this.momentumPhase = 'none';
        this.momentumVelocity = 0;
        this.clearVirtualSpring();
        this.endPhase(null);
    }

    private clearVirtualSpring(): void {
        this.momentumSpringSide = 0;
        this.momentumSpringVisible = 0;
        this.momentumSpringVelocity = 0;
    }

    private addMomentumSample(top: number, time: number): void {
        let samples = this.momentumSamples;
        const last = samples.at(-1);
        const previous = samples.at(-2);
        if (last != null && previous != null
            && (last.top - previous.top) * (top - last.top) < 0) {
            samples = [last];
            this.momentumSamples = samples;
        }

        samples.push({ top, time });
        while (samples.length > MomentumSampleCapacity
            || (samples.length > 2 && time - samples[0].time > MomentumSampleWindowMs))
            samples.shift();
    }

    private estimateMomentumSpeed(): number {
        const samples = this.momentumSamples;
        if (samples.length < 2)
            return this.followSpeed;

        let meanTime = 0;
        let meanTop = 0;
        for (const sample of samples) {
            meanTime += sample.time;
            meanTop += sample.top;
        }
        meanTime /= samples.length;
        meanTop /= samples.length;

        let covariance = 0;
        let variance = 0;
        for (const sample of samples) {
            const t = sample.time - meanTime;
            covariance += t * (sample.top - meanTop);
            variance += t * t;
        }
        return variance > 0 ? covariance / variance : 0;
    }

    private stopMomentumTakeover(mustFlushOverflow = false): void {
        this.momentumSamples = [];
        if (this.momentumPhase !== 'none') {
            this.momentumPhase = 'none';
            this.momentumVelocity = 0;
            this.clearVirtualSpring();
        }
        if (this.isOverflowLocked) {
            ++this.overflowLockEpoch;
            this.setOverflowLocked(false, true);
            if (mustFlushOverflow)
                void this.element.offsetHeight;
        }
    }

    private armLockWatchdog(): void {
        if (this.lockWatchdog !== null)
            clearTimeout(this.lockWatchdog);

        this.lockWatchdog = setTimeout(
            () => {
                this.lockWatchdog = null;
                if (this.phase === 'in-band')
                    return;

                console.warn(`ScrollController: phase '${this.phase}' stopped advancing`
                    + ` (touching: ${this.isTouching}); handing the scroller back.`);
                this.stopMomentumTakeover();
                this.endPhase(this.boundary);
            },
            LockWatchdogMs);
    }

    private tickFollow(time: number): void {
        const scrollTop = this.element.scrollTop;
        const dt = time - this.followTime;
        if (dt > 0) {
            this.followSpeed = (scrollTop - this.followTop) / dt;
            this.followTop = scrollTop;
            this.followTime = time;
        }

        this.follow(scrollTop);
        if (this.isTouching) {
            // A finger still this long is a touchend that never arrived; left alone the list stays parked
            // off its own content.
            if (Math.abs(scrollTop - this.stillTop) > FixPrecisionPx) {
                this.stillTop = scrollTop;
                this.stillSince = time;
            }
            else if (time - this.stillSince > TouchStaleMs) {
                this.onTouchEnd();
            }

            return;
        }

        this.engage(this.followSpeed);
    }

    // The browser's motion is shown through the resistance exactly as a throw is. Outward, the display
    // carries the release's momentum - at least the speed the browser is observed to move it, decaying
    // under the spring - until it turns; the browser's own fling is ended meanwhile (killMomentum), so
    // the carry is what the bounce is made of. Inward, the floor adds only what the motion falls short
    // of: a fast fling home is a throw, a stall is a spring, and there is no handover between them
    // because it is one rule.
    private tickSpring(dt: number, time: number): void {
        const scrollTop = this.element.scrollTop;
        const scrollDelta = scrollTop - this.lastBandTop;
        this.lastBandTop = scrollTop;
        const elapsed = time - this.springLastTime;
        this.springLastTime = time;
        this.advanceSpring(scrollDelta, dt, elapsed);
    }

    private advanceSpring(scrollDelta: number, dt: number, elapsed: number): void {
        this.scrollSpeed = elapsed > 0 ? scrollDelta / elapsed : 0;
        const side = Math.sign(this.over) || Math.sign(scrollDelta);
        if (side * scrollDelta > 0)
            this.killMomentum();

        const before = signedOverscroll(this.over);
        this.over += scrollDelta;
        let displayed = signedOverscroll(this.over);
        if (side !== 0) {
            // Outward-positive from here on.
            const x = side * before;
            const shownX = side * displayed;
            const w = Math.sqrt(ReturnStiffness);
            let v = side * this.carried;
            let carriedX = x;
            // The browser coming home knows better than a carry, and a bounce that has reached its cap
            // is spent; short of both, the browser heading out feeds the carry. Observed over the real
            // time since the last read, not the loop's dt: the first frame after a release has dt = 0
            // and is exactly the one the browser's momentum shows most in.
            if (shownX < x || x >= this.bounceCap)
                v = 0;
            else if (elapsed > 0)
                v = Math.max(v, (shownX - x) * 1000 / elapsed);
            if (v > 0 && dt > 0) {
                v = Math.min(v, MaxCarrySpeedPxS);
                [carriedX, v] = stepCriticalSpring(x, v, dt);
            }

            if (v > 0) {
                displayed = side * Math.min(Math.max(shownX, carriedX), this.bounceCap);
                this.carried = side * v;
            }
            else {
                this.carried = 0;
                if (dt > 0) {
                    const floorSpeed = ReturnFloorFactor * w * shownX;
                    const actualInward = (x - shownX) / dt;
                    const shortfall = Math.max(0, Math.min(floorSpeed, MaxReturnSpeedPxS) - actualInward) * dt;
                    displayed = side * (shownX - Math.min(shortfall, shownX));
                }
            }

            // `over` follows the display through the inverse, so the next frame's resistance is computed
            // for what is actually on screen.
            this.over = rawOverscroll(displayed);
        }

        this.nudge(scrollDelta - (displayed - before));
        if (Math.abs(displayed) <= ReturnSettlePx && scrollDelta === 0)
            this.settle();
    }

    // The one finger-up write: nothing is on screen and the scroll has stopped. Written first, read
    // back, and the transform gives up exactly what landed - if the engine takes none of it (WebKit,
    // just after a fast fling), nothing changes and this runs again next frame.
    private settle(): void {
        const before = this.element.scrollTop;
        this.element.scrollTop = this.boundary;
        const landed = this.element.scrollTop - before;
        if (landed !== 0) {
            this.nudge(landed);
            this.lastBandTop = this.element.scrollTop;
            this.lastScrollTop = this.element.scrollTop;
        }

        // Within a device pixel is landed: the boundary need not sit on one.
        if (Math.abs(this.element.scrollTop - this.boundary) <= 1)
            this.endPhase(null);
    }

    // Hands the element back, at `scrollTop` if given. Null leaves the position alone.
    private endPhase(scrollTop: number | null, mustKeepOverflowLocked = false): void {
        this.phase = 'in-band';
        this.over = 0;
        this.carried = 0;
        this.followSpeed = 0;
        if (this.lockWatchdog !== null) {
            clearTimeout(this.lockWatchdog);
            this.lockWatchdog = null;
        }

        // The write goes first and only what landed comes out of the transform, so a refused write is
        // not a step.
        if (scrollTop !== null && Math.abs(this.element.scrollTop - scrollTop) > FixPrecisionPx) {
            const before = this.element.scrollTop;
            this.element.scrollTop = scrollTop;
            this.nudge(this.element.scrollTop - before);
        }

        this.applyTranslate(0);
        if (!mustKeepOverflowLocked)
            this.setOverflowLocked(false, true);

        this.lastScrollTop = this.element.scrollTop;
        this.lastScrollTime = performance.now();
        this.recentSpeed = 0;
    }

    // The one way the band moves: by a delta to the transform, never by assigning it. The pair
    // (scrollTop, transform) is the position on screen; this adjusts the second term by exactly what
    // the frame intends, and whatever else the transform holds is untouched.
    private nudge(delta: number): void {
        if (delta !== 0)
            this.applyTranslate(this.overscrollOffset + delta);
    }

    private cancelMomentum(): void {
        if (!this.enableScrollConstraints || this.phase !== 'in-band' || this.isTouching)
            return;

        this.killMomentum();
    }

    // Ends a fling where the ordinary return path can. WebKit is handled by the release takeover instead;
    // writing scrollTop back to itself is neither a reliable stop nor harmless to its compositor.
    private killMomentum(): void {
        if (!canLockOverflow() || this.isOverflowLocked)
            return;

        const epoch = ++this.overflowLockEpoch;
        this.setOverflowLocked(true);
        // The lock has to land in this frame, not in the one the unlock is already scheduled for.
        void this.element.offsetHeight;
        let frames = MomentumKillFrames;
        const unlock = () => {
            if (epoch !== this.overflowLockEpoch)
                return;

            if (--frames > 0) {
                fastRaf({ write: unlock });
                return;
            }

            // Released as soon as it has done its job, whatever phase that leaves. The lock ends a
            // fling and nothing else, so holding it for the rest of a return costs the next gesture:
            // an element that was unscrollable when the finger landed is given no scrolling for that
            // whole gesture, and releasing at touchstart comes too late - the compositor has already
            // decided. Measured on Android: a finger landing on a spring with 1.6px left to run then
            // dragged 397px without moving scrollTop by a pixel. Documented as WebKit-only; it is not.
            this.setOverflowLocked(false);
        };
        fastRaf({ write: unlock });
    }

    // WebKit can leave a paint-contained, composited scroller unrastered after a programmatic scrollTop
    // write: on iOS the chat view lands on the new position showing nothing, and only the next touch
    // brings it back. The offsetHeight read above forces layout, not paint, so it can't help - invalidating
    // the layer can. The rubber-band owns this same property, so this stays out of its way in both
    // directions: it doesn't start one while the band is active, and it only clears a value it still owns.
    private nudgeRepaint(): void {
        if (!DeviceInfo.isWebKit || this.isTouching || this.phase !== 'in-band')
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
        this.onTransform?.();
    }

    private setOverflowLocked(locked: boolean, mustForce = false): void {
        if ((!mustForce && !canLockOverflow()) || this.isOverflowLocked === locked)
            return;

        this.isOverflowLocked = locked;
        this.element.style.overflowY = locked ? 'hidden' : '';
    }
}

// Whether the ordinary return path may switch the scroller off.
//
// Not on WebKit, where it costs more than it buys. An element that was overflow:hidden when a finger
// landed refuses to scroll for that entire gesture, so the ordinary return unlocks immediately after
// ending a fling. The WebKit release takeover deliberately holds its forced lock while the content
// transform returns. Overridable as ?vllock=0 or ?vllock=1.
let overflowLockEnabled: boolean | null = null;

function canLockOverflow(): boolean {
    if (overflowLockEnabled !== null)
        return overflowLockEnabled;

    const value = new URLSearchParams(location.search).get('vllock');
    if (value === '0')
        overflowLockEnabled = false;
    else if (value === '1')
        overflowLockEnabled = true;
    else
        overflowLockEnabled = !DeviceInfo.isWebKit;

    return overflowLockEnabled;
}

function canTakeOverMomentum(): boolean {
    const value = new URLSearchParams(location.search).get('vltakeover');
    if (value === '0')
        return false;
    if (value === '1')
        return true;

    return DeviceInfo.isIos && DeviceInfo.isWebKit;
}

function readFriction(maxDefault: number, rampDefault: number): [number, number] {
    const value = new URLSearchParams(location.search).get('vlfriction');
    const match = value == null ? null : /^([\d.]+)x([\d.]+)$/.exec(value);
    if (match == null)
        return [maxDefault, rampDefault];

    const max = Number.parseFloat(match[1]);
    const ramp = Number.parseFloat(match[2]);
    return [max > 0 && max < 1 ? max : maxDefault, ramp > 0 ? ramp : rampDefault];
}

// Pull-back distance - the part the resistance eats, and exactly what the transform has to carry.
function resistancePull(over: number): number {
    return over <= ResistanceRampPx
        ? (MaxResistance * over * over) / (2 * ResistanceRampPx)
        : MaxResistance * over - (MaxResistance * ResistanceRampPx) / 2;
}

// What is actually on screen for a raw pull of `over` (>= 0).
function visibleOverscroll(over: number): number {
    return over - resistancePull(over);
}

// The same, for a pull that carries its own direction.
function signedOverscroll(over: number): number {
    return Math.sign(over) * visibleOverscroll(Math.abs(over));
}

// How much of the next pixel of pull reaches the screen - the curve's slope at `over` (>= 0).
function visibleSlope(over: number): number {
    return 1 - Math.min((MaxResistance * over) / ResistanceRampPx, MaxResistance);
}

// signedOverscroll backwards: the raw pull whose resisted image is `visible`. The spring moves what is
// on screen, and `over` has to follow through this so the next frame's resistance is computed for the
// pull that is actually being shown.
function rawOverscroll(visible: number): number {
    const v = Math.abs(visible);
    const raw = v <= ResistanceRampPx * (1 - MaxResistance / 2)
        ? (ResistanceRampPx / MaxResistance) * (1 - Math.sqrt(1 - (2 * MaxResistance * v) / ResistanceRampPx))
        : (v - (MaxResistance * ResistanceRampPx) / 2) / (1 - MaxResistance);
    return Math.sign(visible) * raw;
}

function stepCriticalSpring(position: number, velocity: number, dt: number): [number, number] {
    const w = Math.sqrt(ReturnStiffness);
    const b = velocity + w * position;
    const decay = Math.exp(-w * dt);
    return [
        (position + b * dt) * decay,
        (velocity - w * b * dt) * decay,
    ];
}

(globalThis as unknown as { ScrollController: typeof ScrollController }).ScrollController = ScrollController;

import { clamp } from 'math';
import { fastRaf } from 'fast-raf';

export interface ScrollToOptions {
    smooth?: boolean;
    clamp?: boolean;
    offset?: number;
    lock?: boolean;
}

const BaseLockPeriodMs = 250;
const MinLockPeriodMs = 50;
const VerifyFrames = 2;
const FixPrecisionPx = 0.1;

// Binds to one scrollable element. When scroll lock is enabled, constrains scrollTop to a [min, max]
// band: an out-of-band scroll is pinned to the edge with overflow:hidden (which cancels the inertial
// fling) for a lock period, then overflow is restored to verify the position holds over the next few
// frames; if it drifts back out, it re-locks for half the period and retries, until the period drops
// below the min.
export class ScrollController {
    private static readonly all = new Set<ScrollController>();

    private readonly abort = new AbortController();

    private min: number | null = null;
    private max: number | null = null;

    private _lockedScrollTop: number | null = null;
    private lockPeriodMs = 0;
    private lockPhaseEndsAt = 0;
    private verifyFramesLeft = 0;
    private scrollLockEpoch = 0;
    private isOverflowLocked = false;

    constructor(
        private readonly element: HTMLElement,
        private readonly enableScrollLock = false,
    ) {
        if (this.enableScrollLock) {
            const passiveOpts = { passive: true, signal: this.abort.signal };
            element.addEventListener('scroll', () => this.onScroll(), passiveOpts);
        }
        ScrollController.all.add(this);
    }

    public get isScrollLocked(): boolean {
        return this._lockedScrollTop !== null;
    }

    public get lockedScrollTop(): number | null {
        return this._lockedScrollTop;
    }

    public dispose(): void {
        this.stopScrollLock();
        this.abort.abort();
        ScrollController.all.delete(this);
    }

    public static startScrollLockAll(): void {
        for (const controller of this.all)
            controller.startScrollLock(controller.element.scrollTop);
    }

    public scrollTo(target: number | HTMLElement, options: ScrollToOptions = {}): void {
        if (typeof target === 'number') {
            let top = target + (options.offset ?? 0);
            if (options.clamp !== false) {
                const limits = this.getCurrentScrollLimits();
                top = clamp(top, limits.min, limits.max);
            }
            if (options.smooth === false) {
                this.element.scrollTop = top;
                if (options.lock)
                    this.startScrollLock(top);
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

    public setScrollLimits(min: number | null, max: number | null): void {
        this.min = min;
        this.max = max;
        this.fixScrollTop();
    }

    public getScrollLimits(): { min: number | null, max: number | null } {
        return { min: this.min, max: this.max }
    }

    public getCurrentScrollLimits(): { min: number, max: number } {
        const min = this.min ?? 0;
        const max = this.max ?? this.element.scrollHeight - this.element.clientHeight;
        return {
            min: min,
            max: max < min ? min : max,
        };
    }

    public startScrollLock(scrollTop: number): void {
        if (!this.enableScrollLock)
            return;

        const wasRunning = this._lockedScrollTop !== null;
        this._lockedScrollTop = scrollTop;
        this.lockPeriodMs = BaseLockPeriodMs;
        this.lockPhaseEndsAt = Date.now() + BaseLockPeriodMs;
        this.verifyFramesLeft = 0;
        this.setOverflowLocked(true);
        this.pinScrollTop();
        if (!wasRunning)
            this.runScrollLock(++this.scrollLockEpoch);
    }

    public stopScrollLock(): void {
        this._lockedScrollTop = null;
        this.setOverflowLocked(false);
        ++this.scrollLockEpoch;
    }

    // Private methods

    private onScroll(): void {
        this.fixScrollTop();
    }

    private fixScrollTop(): boolean {
        if (!this.enableScrollLock || this.isScrollLocked)
            return false;

        const scrollTop = this.element.scrollTop;
        const limits = this.getCurrentScrollLimits();
        const clampedScrollTop = clamp(scrollTop, limits.min, limits.max);
        if (Math.abs(scrollTop - clampedScrollTop) <= FixPrecisionPx)
            return false;

        this.startScrollLock(clampedScrollTop);
        return true;
    }

    private runScrollLock(epoch: number): void {
        const tick = () => {
            if (epoch !== this.scrollLockEpoch || this._lockedScrollTop === null)
                return;

            const now = Date.now();
            if (this.verifyFramesLeft === 0) {
                this.pinScrollTop();
                if (now >= this.lockPhaseEndsAt) {
                    this.verifyFramesLeft = VerifyFrames;
                    this.setOverflowLocked(false);
                }
            }
            else if (Math.abs(this.element.scrollTop - this._lockedScrollTop) <= FixPrecisionPx) {
                if (--this.verifyFramesLeft === 0) {
                    this.stopScrollLock();
                    return;
                }
            }
            else {
                const nextPeriod = this.lockPeriodMs / 2;
                if (nextPeriod < MinLockPeriodMs) {
                    this.stopScrollLock();
                    return;
                }
                this.lockPeriodMs = nextPeriod;
                this.lockPhaseEndsAt = now + nextPeriod;
                this.verifyFramesLeft = 0;
                this.setOverflowLocked(true);
                this.pinScrollTop();
            }
            fastRaf({ write: tick });
        };
        fastRaf({ write: tick });
    }

    private pinScrollTop(): void {
        if (this._lockedScrollTop === null)
            return;

        const limits = this.getCurrentScrollLimits();
        const pinned = clamp(this._lockedScrollTop, limits.min, limits.max);
        this._lockedScrollTop = pinned;
        if (Math.abs(this.element.scrollTop - pinned) > FixPrecisionPx) {
            this.element.scrollTop = pinned;
            void this.element.offsetHeight; // Triggers reflow
        }
    }

    private setOverflowLocked(locked: boolean): void {
        if (this.isOverflowLocked === locked)
            return;

        this.isOverflowLocked = locked;
        this.element.style.overflowY = locked ? 'hidden' : '';
    }
}

(globalThis as unknown as { ScrollController: typeof ScrollController }).ScrollController = ScrollController;

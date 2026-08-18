// Two things make a virtual list unsafe to reposition: a height animation, because the DOM is then
// deliberately out of step with the model it is animating towards, and a scroll, because writing
// scrollTop under a live gesture or a smooth scroll fights it. Everything that moves the view asks
// here first; everything that moves declares a deadline, so a lost transitionend can't wedge the list.
export class StabilityTracker {
    private readonly animations = new Map<string, number>();
    private scrollUntil = 0;
    private waiters = new Array<{ needsStable: boolean; resolve: () => void }>();
    private timer: ReturnType<typeof setTimeout> | null = null;

    public dispose(): void {
        this.animations.clear();
        this.scrollUntil = 0;
        this.clearTimer();
        const waiters = this.waiters;
        this.waiters = [];
        for (const waiter of waiters)
            waiter.resolve();
    }

    public get isAnimating(): boolean {
        this.sweep();
        return this.animations.size > 0;
    }

    public get isScrolling(): boolean {
        this.sweep();
        return this.scrollUntil > 0;
    }

    public get isStable(): boolean {
        this.sweep();
        return this.animations.size === 0 && this.scrollUntil === 0;
    }

    public holdAnimation(key: string, durationMs: number): void {
        this.animations.set(key, performance.now() + durationMs);
        this.rearm();
    }

    public releaseAnimation(key: string): void {
        if (!this.animations.delete(key))
            return;

        this.notify();
    }

    public releaseAllAnimations(): void {
        if (this.animations.size === 0)
            return;

        this.animations.clear();
        this.notify();
    }

    public holdScroll(durationMs: number): void {
        this.scrollUntil = Math.max(this.scrollUntil, performance.now() + durationMs);
        this.rearm();
    }

    public releaseScroll(): void {
        if (this.scrollUntil === 0)
            return;

        this.scrollUntil = 0;
        this.notify();
    }

    public whenNoAnimations(): Promise<void> {
        if (!this.isAnimating)
            return Promise.resolve();

        return new Promise<void>(resolve => this.waiters.push({ needsStable: false, resolve }));
    }

    public whenStable(): Promise<void> {
        if (this.isStable)
            return Promise.resolve();

        return new Promise<void>(resolve => this.waiters.push({ needsStable: true, resolve }));
    }

    // Private methods

    private notify(): void {
        const isAnimating = this.isAnimating;
        const isStable = this.isStable;
        if (isAnimating && !isStable)
            return;

        const kept = new Array<{ needsStable: boolean; resolve: () => void }>();
        const released = new Array<() => void>();
        for (const waiter of this.waiters) {
            if (waiter.needsStable ? isStable : !isAnimating)
                released.push(waiter.resolve);
            else
                kept.push(waiter);
        }
        this.waiters = kept;
        for (const resolve of released)
            resolve();
    }

    private sweep(): void {
        const now = performance.now();
        if (this.scrollUntil !== 0 && this.scrollUntil <= now)
            this.scrollUntil = 0;
        if (this.animations.size === 0)
            return;

        for (const [key, deadline] of [...this.animations])
            if (deadline <= now)
                this.animations.delete(key);
    }

    private rearm(): void {
        this.clearTimer();
        let earliest = this.scrollUntil === 0 ? Infinity : this.scrollUntil;
        for (const deadline of this.animations.values())
            earliest = Math.min(earliest, deadline);
        if (!Number.isFinite(earliest))
            return;

        this.timer = setTimeout(
            () => {
                this.timer = null;
                this.notify();
                this.rearm();
            },
            Math.max(0, earliest - performance.now()) + 1);
    }

    private clearTimer(): void {
        if (this.timer === null)
            return;

        clearTimeout(this.timer);
        this.timer = null;
    }
}

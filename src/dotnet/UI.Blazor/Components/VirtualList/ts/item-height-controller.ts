import { getLogs } from 'logging';
import { StabilityTracker } from './stability-tracker';

const { debugLog, errorLog } = getLogs('InfiniteList');

// Must be >= the CSS `transition: height ...` duration; read from a live item and cached, so the
// number lives in exactly one place (virtual-list.css).
const FallbackTransitionMs = 150;
const TransitionSlackMs = 60;
// How long a change waits before it is written, measured from the first change rather than the last:
// content that keeps moving - a live transcript - has to be followed, so this rate-limits writes
// instead of waiting for quiet. It still absorbs the burst a fresh item goes through while its avatar,
// header and text settle, and the shrink-then-regrow of re-recognition.
const DefaultSettleDelayMs = 100;
const HeightEpsilon = 0.5;

interface ItemHeightState {
    ref: HTMLElement;
    contentRef: HTMLElement;
    settleDelayMs: number;
    mustAnimateChanges: boolean;
    intrinsic: number;
    applied: number;
    isControlled: boolean;
    isUnsettled: boolean;
    isAppearing: boolean;
    animatingUntil: number;
    animationTimer: ReturnType<typeof setTimeout> | null;
    timer: ReturnType<typeof setTimeout> | null;
}

// Owns `style.height` of every item in a list that animates heights. What the list's geometry model
// reads is the settled height - where the item will be once the transition lands - so an animation in
// flight changes how the list looks without changing where it thinks anything is.
export class ItemHeightController {
    private readonly states = new Map<string, ItemHeightState>();
    private readonly contentObserver: ResizeObserver;
    private readonly domObserver: MutationObserver;
    private readonly contentKeys = new WeakMap<Element, string>();
    private readonly deferred = new Set<string>();
    private transitionMs = 0;
    private isSuspended = false;
    private isBatching = false;

    constructor(
        private readonly containerRef: HTMLElement,
        private readonly stability: StabilityTracker,
        private readonly isVisible: (key: string) => boolean,
        private readonly onHeightChanged: (key: string) => void,
    ) {
        this.contentObserver = new ResizeObserver(this.onContentResize);
        // Blazor rewrites an item's whole class attribute whenever its own classes change - the edge
        // classes come and go as the loaded window moves - which silently drops the ones written here.
        // Presence classes flip an item's padding the same way, without the content resizing.
        this.domObserver = new MutationObserver(this.onItemDomChanged);
        this.domObserver.observe(containerRef, {
            attributes: true,
            attributeFilter: ['class'],
            attributeOldValue: true,
            childList: true,
            subtree: true,
        });
        this.containerRef.addEventListener('transitionend', this.onTransitionEnd);
        this.containerRef.addEventListener('transitioncancel', this.onTransitionEnd);
    }

    public dispose(): void {
        this.contentObserver.disconnect();
        this.domObserver.disconnect();
        this.containerRef.removeEventListener('transitionend', this.onTransitionEnd);
        this.containerRef.removeEventListener('transitioncancel', this.onTransitionEnd);
        for (const key of [...this.states.keys()])
            this.untrack(key);
    }

    // What the item occupies in the chain once it has settled - the height we have written, or the one
    // we are about to. Deliberately not the raw intrinsic value: the written height is rounded, and a
    // model carrying the unrounded one drifts from the DOM by half a pixel per item.
    public getHeight(key: string): number | null {
        const state = this.states.get(key);
        if (state == null)
            return null;

        return state.isControlled ? state.applied : toWritableHeight(state.intrinsic);
    }

    public track(key: string, itemRef: HTMLElement): void {
        const existing = this.states.get(key);
        if (existing != null) {
            // The content element is checked as well as the item: a render can keep the item (same
            // @key) and swap what it renders inside it, and an observer left on the detached old one
            // measures 0 forever - collapsing the live item to nothing with no way back.
            if (existing.ref === itemRef && existing.contentRef === itemRef.firstElementChild) {
                this.reassertClasses(existing);
                existing.settleDelayMs = parseDelay(itemRef.dataset.vlHDelay, DefaultSettleDelayMs);
                existing.mustAnimateChanges = itemRef.dataset.vlHTransition !== 'appearance';
                return;
            }

            this.untrack(key);
        }

        const contentRef = getContentRef(key, itemRef);
        if (contentRef == null) {
            // Nothing to measure - the item renders conditionally and this render produced no content.
            // Any height left on it from before would be a phantom box the list keeps reserving.
            release(itemRef);
            return;
        }

        this.states.set(key, {
            ref: itemRef,
            contentRef,
            settleDelayMs: parseDelay(itemRef.dataset.vlHDelay, DefaultSettleDelayMs),
            mustAnimateChanges: itemRef.dataset.vlHTransition !== 'appearance',
            intrinsic: -1,
            applied: Number.NaN,
            isControlled: false,
            isUnsettled: false,
            isAppearing: false,
            animatingUntil: 0,
            animationTimer: null,
            timer: null,
        });
        this.contentKeys.set(contentRef, key);
        this.contentObserver.observe(contentRef, { box: 'border-box' });
    }

    public untrack(key: string): void {
        const state = this.states.get(key);
        if (state == null)
            return;

        this.cancelTimer(state);
        this.clearAnimationTimer(state);
        this.stability.releaseAnimation(key);
        this.contentObserver.unobserve(state.contentRef);
        this.states.delete(key);
    }

    // A render can change an item's content without the observer having fired yet, and the model has
    // to be right by the time that same render lays the chain out - so heights are re-read here rather
    // than waited for.
    public remeasure(key: string): number | null {
        const state = this.states.get(key);
        if (state == null)
            return null;

        const height = state.contentRef.getBoundingClientRect().height + getOuterExtra(state);
        this.setIntrinsic(key, state, height);
        return this.getHeight(key);
    }

    // Parks the item at `startHeight` so it grows in from there: 0 for an item that genuinely arrived,
    // the outgoing item's height for one that replaced another.
    public beginAppearance(key: string, itemRef: HTMLElement, startHeight: number): void {
        this.track(key, itemRef);
        const state = this.states.get(key);
        if (state == null || this.isSuspended)
            return;

        state.isAppearing = true;
        this.write(key, state, startHeight, false);
        debugLog?.log(`height #${key}: appear from ${startHeight}px`);
        // A transition runs from a previous computed style, and a brand-new element has none: without
        // resolving the start height first the browser only ever sees the final one and the item pops.
        void itemRef.offsetHeight;
        this.schedule(key, state);
    }

    // Whether an item should animate depends on whether it is on screen, and mid-render the list's
    // geometry model is half-rebuilt - so from here until endBatch every decision is held.
    public beginBatch(): void {
        this.isBatching = true;
    }

    public endBatch(): void {
        this.isBatching = false;
        const keys = [...this.deferred];
        this.deferred.clear();
        for (const key of keys) {
            const state = this.states.get(key);
            if (state != null)
                this.schedule(key, state);
        }
    }

    // While a reposition waits for the list to stop moving, changes still to come land instantly:
    // starting new animations would only keep pushing that moment away. Transitions already running are
    // left to finish - they are what the reposition is waiting out.
    public suspend(): void {
        if (this.isSuspended)
            return;

        this.isSuspended = true;
        this.applyInstantly(state => !isAnimating(state));
    }

    public resume(): void {
        this.isSuspended = false;
    }

    public applyAllInstantly(): void {
        this.applyInstantly(() => true);
    }

    // Private methods

    private setUnsettled(state: ItemHeightState, isUnsettled: boolean): void {
        state.isUnsettled = isUnsettled;
        state.ref.classList.toggle('c-height-unsettled', isUnsettled);
    }

    // Re-applies what Blazor's class rewrite drops. Without this the item keeps the height written to
    // it while losing the rules that make that height mean anything: the min-height floor comes back,
    // the clip goes, and the content spills over the item below.
    private reassertClasses(state: ItemHeightState): void {
        if (state.isControlled)
            state.ref.classList.add('c-height-controlled');
        if (state.isUnsettled)
            state.ref.classList.add('c-height-unsettled');
    }

    private onItemDomChanged = (mutations: MutationRecord[]): void => {
        for (const mutation of mutations) {
            const itemRef = mutation.target as HTMLElement;
            const key = itemRef.dataset.key ?? '';
            const state = this.states.get(key);
            if (state?.ref !== itemRef)
                continue;

            if (mutation.type === 'childList') {
                // A component nested inside the item can swap what it renders without the list
                // rendering at all, which leaves the observer measuring a detached element forever.
                if (state.contentRef !== itemRef.firstElementChild)
                    this.track(key, itemRef);
                continue;
            }

            // Our own writes are the common case here and must not feed back into a remeasure loop.
            if (!hasForeignClassChange(mutation.oldValue, itemRef.className))
                continue;

            this.reassertClasses(state);
            // The item's own padding is part of the height it needs, and a class can change it without
            // the content ever resizing - so the chrome has to be re-read, not assumed.
            this.remeasure(key);
        }
    };

    private applyInstantly(canApply: (state: ItemHeightState) => boolean): void {
        const written = new Array<HTMLElement>();
        for (const [key, state] of this.states) {
            if (!canApply(state))
                continue;

            this.cancelTimer(state);
            this.clearAnimationTimer(state);
            state.isAppearing = false;
            state.animatingUntil = 0;
            this.stability.releaseAnimation(key);
            this.setUnsettled(state, false);
            const target = toWritableHeight(state.intrinsic);
            if (target == null || (state.applied === target && state.isControlled))
                continue;

            state.applied = target;
            state.isControlled = true;
            state.ref.classList.add('c-height-controlled');
            this.setUnsettled(state, false);
            state.ref.style.transition = 'none';
            state.ref.style.height = `${target}px`;
            written.push(state.ref);
            this.onHeightChanged(key);
        }
        if (written.length === 0)
            return;

        // One flush for the whole batch: doing it per item would cost a forced layout each time.
        void this.containerRef.offsetHeight;
        for (const itemRef of written)
            itemRef.style.transition = '';
    }

    private onContentResize = (entries: ResizeObserverEntry[]): void => {
        for (const entry of entries) {
            const key = this.contentKeys.get(entry.target);
            const state = key == null ? undefined : this.states.get(key);
            if (key == null || state == null)
                continue;

            const contentHeight = entry.borderBoxSize.length > 0
                ? entry.borderBoxSize[0].blockSize
                : entry.contentRect.height;
            this.setIntrinsic(key, state, contentHeight + getOuterExtra(state));
        }
    };

    private setIntrinsic(key: string, state: ItemHeightState, intrinsic: number): void {
        // A zero from an element that isn't laid out is the list being hidden, not the content being
        // empty - believing it would write every item to nothing and collapse the chain.
        if (intrinsic <= 0 && state.contentRef.getClientRects().length === 0)
            return;

        const wasKnown = state.intrinsic >= 0;
        if (wasKnown && Math.abs(intrinsic - state.intrinsic) < HeightEpsilon)
            return;

        state.intrinsic = intrinsic;
        // Before the first write there is nothing written to model, so the intrinsic value is what the
        // list has to go on; afterwards only `write` moves the settled height.
        if (!wasKnown && !state.isControlled)
            this.onHeightChanged(key);
        this.schedule(key, state);
    }

    private schedule(key: string, state: ItemHeightState): void {
        if (state.intrinsic < 0)
            return;

        if (this.isBatching)
            this.deferred.add(key);
        else
            this.scheduleNow(key, state);
    }

    private scheduleNow(key: string, state: ItemHeightState): void {
        const mustAnimate = !this.isSuspended
            && this.isVisible(key)
            && (state.isAppearing || (state.mustAnimateChanges && state.isControlled));
        if (!mustAnimate) {
            this.cancelTimer(state);
            state.isAppearing = false;
            this.stability.releaseAnimation(key);
            this.write(key, state, state.intrinsic, false);
            return;
        }

        // Already showing what the content wants - so no clip, no timer, and nothing for a reposition
        // to wait on. Checked before arming anything, or every settled animation would re-arm a whole
        // cycle just to discover there was nothing to write.
        if (toWritableHeight(state.intrinsic) === state.applied) {
            this.cancelTimer(state);
            state.isAppearing = false;
            this.setUnsettled(state, false);
            this.stability.releaseAnimation(key);
            return;
        }

        // Clipped from here rather than from when the transition starts, and before the early return
        // below: for the whole settle delay - and for as long as a transition already in flight owns
        // the item - the written height is behind the content, and an unclipped item paints the
        // difference straight over whatever comes after it.
        this.setUnsettled(state, true);
        // A running transition owns the item until it lands; onTransitionEnd re-schedules with whatever
        // the content settled on meanwhile, so acting here would only retarget it midway.
        if (isAnimating(state) || state.timer != null)
            return;

        state.timer = setTimeout(
            () => {
                state.timer = null;
                if (this.states.get(key) === state)
                    this.run(key, state);
            },
            state.settleDelayMs);
        // The pending write counts as an animation: a reposition that ran in the gap would be undone
        // by the one it did not wait for.
        this.stability.holdAnimation(key, state.settleDelayMs + this.getTransitionMs() + TransitionSlackMs);
    }

    private run(key: string, state: ItemHeightState): void {
        state.isAppearing = false;
        // Keyed off what write() would actually do, not off the raw difference: a sub-pixel change can
        // round to the height already applied, and marking that a running animation would leave the
        // item waiting for a transitionend that never comes - and frozen at that height for good.
        if (!this.write(key, state, state.intrinsic, true)) {
            this.setUnsettled(state, false);
            this.stability.releaseAnimation(key);
            return;
        }

        // Backed by a timer, not just a deadline: a transitionend can fail to arrive at all - the
        // element was detached mid-flight, or nothing ever rendered it so no transition started - and
        // with nothing firing at the deadline the item would keep the clip for good, and a change that
        // arrived while it ran would never be written.
        const duration = this.getTransitionMs() + TransitionSlackMs;
        state.animatingUntil = performance.now() + duration;
        this.clearAnimationTimer(state);
        state.animationTimer = setTimeout(
            () => {
                state.animationTimer = null;
                if (this.states.get(key) === state)
                    this.settle(key, state);
            },
            duration);
        debugLog?.log(`height #${key}: -> ${state.applied}px`);
        this.stability.holdAnimation(key, duration);
    }

    private settle(key: string, state: ItemHeightState): void {
        this.clearAnimationTimer(state);
        state.animatingUntil = 0;
        this.setUnsettled(state, false);
        this.stability.releaseAnimation(key);
        this.schedule(key, state);
    }

    private onTransitionEnd = (event: Event): void => {
        const transitionEvent = event as TransitionEvent;
        if (transitionEvent.propertyName !== 'height')
            return;

        const itemRef = transitionEvent.target as HTMLElement;
        const key = itemRef.dataset.key ?? '';
        const state = this.states.get(key);
        if (state?.ref !== itemRef)
            return;

        if (state.animatingUntil === 0) {
            this.setUnsettled(state, false);
            return;
        }

        this.settle(key, state);
    };

    // Returns whether the height actually changed, i.e. whether a transition can be expected to run.
    private write(key: string, state: ItemHeightState, height: number, isAnimated: boolean): boolean {
        const target = Math.max(0, Math.round(height));
        if (state.applied === target && state.isControlled)
            return false;

        const itemRef = state.ref;
        state.applied = target;
        state.isControlled = true;
        itemRef.classList.add('c-height-controlled');
        this.onHeightChanged(key);
        if (isAnimated) {
            this.setUnsettled(state, true);
            itemRef.style.height = `${target}px`;
            return true;
        }

        // Suppressed on the element, not via a class: this has to hold for exactly one write, and the
        // next one may well need to animate.
        this.setUnsettled(state, false);
        itemRef.style.transition = 'none';
        itemRef.style.height = `${target}px`;
        void itemRef.offsetHeight;
        itemRef.style.transition = '';
        return true;
    }

    private getTransitionMs(): number {
        if (this.transitionMs > 0)
            return this.transitionMs;

        for (const state of this.states.values()) {
            if (!state.isControlled)
                continue;

            const seconds = Number.parseFloat(getComputedStyle(state.ref).transitionDuration);
            if (Number.isFinite(seconds) && seconds > 0) {
                this.transitionMs = seconds * 1000;
                return this.transitionMs;
            }
        }

        return FallbackTransitionMs;
    }

    private clearAnimationTimer(state: ItemHeightState): void {
        if (state.animationTimer == null)
            return;

        clearTimeout(state.animationTimer);
        state.animationTimer = null;
    }

    private cancelTimer(state: ItemHeightState): void {
        if (state.timer == null)
            return;

        clearTimeout(state.timer);
        state.timer = null;
    }
}

// A transition is only believed to be running until its deadline: a transitionend can be lost outright
// - the element detaches mid-flight, or nothing ever rendered it so no transition started at all - and
// a flag with no expiry would then refuse every later write and freeze the item at that height.
function isAnimating(state: ItemHeightState): boolean {
    return state.animatingUntil > performance.now();
}

// Whether a class-attribute change was somebody else's. Ours are the two markers written here, and
// reacting to those would put every write into a loop with the observer that reports it.
function hasForeignClassChange(oldValue: string | null, newValue: string): boolean {
    return withoutOwnClasses(oldValue ?? '') !== withoutOwnClasses(newValue);
}

function withoutOwnClasses(value: string): string {
    return value
        .split(/\s+/)
        .filter(x => x !== '' && x !== 'c-height-controlled' && x !== 'c-height-unsettled')
        .sort()
        .join(' ');
}

// Hands the item back to the stylesheet: whatever height was written to it describes content that is
// no longer there.
function release(itemRef: HTMLElement): void {
    itemRef.classList.remove('c-height-controlled', 'c-height-unsettled');
    itemRef.style.height = '';
}

// Everything the item has to reserve beyond the content element's own box: the padding and border it
// adds around it, and the content element's margins. Those margins are inside the item - an item is a
// flex item, so it establishes its own formatting context and nothing collapses out of it - and
// leaving them out is how content ends up painted over whatever follows.
function getOuterExtra(state: ItemHeightState): number {
    const itemStyle = getComputedStyle(state.ref);
    const contentStyle = getComputedStyle(state.contentRef);
    return (parseFloat(itemStyle.paddingTop) || 0)
        + (parseFloat(itemStyle.paddingBottom) || 0)
        + (parseFloat(itemStyle.borderTopWidth) || 0)
        + (parseFloat(itemStyle.borderBottomWidth) || 0)
        + (parseFloat(contentStyle.marginTop) || 0)
        + (parseFloat(contentStyle.marginBottom) || 0);
}

// An item renders exactly one element, and that element is what carries its intrinsic height - the
// item's own box says nothing once we drive it. A second child is content the item would be sized as
// if it did not have: clipped, and unreachable by a scroll-to.
function getContentRef(key: string, itemRef: HTMLElement): HTMLElement | null {
    const children = itemRef.children;
    if (children.length !== 1)
        errorLog?.log(`item #${key} must render exactly one element, got ${children.length}`, itemRef);

    return children.length > 0 ? children[0] as HTMLElement : null;
}

function parseDelay(value: string | undefined, fallback: number): number {
    const parsed = Number.parseInt(value ?? '', 10);
    return Number.isFinite(parsed) ? parsed : fallback;
}

function toWritableHeight(height: number): number | null {
    return height < 0 ? null : Math.max(0, Math.round(height));
}

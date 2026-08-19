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
// How many items may animate their height at once. Everything past it is written to its real height
// on the spot. Expanding a conversation turns one item into a whole thread, and animating all of them
// buys nothing over animating the first few - the direction of the change is already obvious - while
// costing a full-height transition per item.
const MaxAnimatedItems = 3;

interface ItemHeightState {
    ref: HTMLElement;
    contentRef: HTMLElement;
    settleDelayMs: number;
    mustAnimateChanges: boolean;
    mustAnimateAppearance: boolean;
    intrinsic: number;
    applied: number;
    isControlled: boolean;
    isUnsettled: boolean;
    isAppearing: boolean;
    animatingUntil: number;
    animationTimer: ReturnType<typeof setTimeout> | null;
    timer: ReturnType<typeof setTimeout> | null;
    // Padding, borders and content margins, or -1 when they must be read again. Cached because
    // measureItems re-measures every item on every render and this resolves two computed styles.
    outerExtra: number;
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
    private pendingRestore: HTMLElement[] | null = null;

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
        // What the item was showing before its element was replaced. The element is new; the item is
        // not, and its height has to carry on from where it was or the swap lands as a step.
        let adopted: number | null = null;
        if (existing != null) {
            // The content element is checked as well as the item: a render can keep the item (same
            // @key) and swap what it renders inside it, and an observer left on the detached old one
            // measures 0 forever - collapsing the live item to nothing with no way back.
            if (existing.ref === itemRef && existing.contentRef === itemRef.firstElementChild) {
                this.reassertClasses(existing);
                existing.settleDelayMs = parseDelay(itemRef.dataset.vlHDelay, DefaultSettleDelayMs);
                const transition = itemRef.dataset.vlHTransition;
                existing.mustAnimateChanges = mustAnimateChanges(transition);
                existing.mustAnimateAppearance = mustAnimateAppearance(transition);
                return;
            }

            if (existing.isControlled && Number.isFinite(existing.applied)) {
                // Where the old element stood, which mid-transition is not the height it was given:
                // adopting the target would land the swap at the end of a movement the reader is still
                // watching. Read only then, so an ordinary render pays no layout for it.
                const rendered = isAnimating(existing) ? existing.ref.getBoundingClientRect().height : 0;
                adopted = rendered > 0 ? rendered : existing.applied;
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
            mustAnimateChanges: mustAnimateChanges(itemRef.dataset.vlHTransition),
            mustAnimateAppearance: mustAnimateAppearance(itemRef.dataset.vlHTransition),
            intrinsic: -1,
            applied: Number.NaN,
            isControlled: false,
            isUnsettled: false,
            isAppearing: false,
            animatingUntil: 0,
            animationTimer: null,
            timer: null,
            outerExtra: -1,
        });
        this.contentKeys.set(contentRef, key);
        this.contentObserver.observe(contentRef, { box: 'border-box' });
        if (adopted == null)
            return;

        // Put the replacement where its predecessor stood, without a transition, so what follows is a
        // change from that height rather than an arrival at a new one. A conversation summary that
        // updates re-renders its element every time; left to itself, each update was written straight
        // in - a fresh state is neither appearing nor controlled, which is exactly what scheduleNow
        // refuses to animate - so the card stepped between heights instead of moving between them.
        this.write(key, this.states.get(key)!, adopted, false);
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
    // the outgoing item's height for one that replaced another. Returns whether it was parked at all -
    // past the animation budget it is left at its own height, and the caller has nothing to re-anchor
    // around. Slots go in call order, which the caller makes top to bottom.
    public beginAppearance(key: string, itemRef: HTMLElement, startHeight: number): boolean {
        this.track(key, itemRef);
        const state = this.states.get(key);
        if (state == null || this.isSuspended || !state.mustAnimateAppearance)
            return false;
        if (!this.hasAnimationSlot(state) && !this.takeSlotFromChange())
            return false;

        state.isAppearing = true;
        this.write(key, state, startHeight, false);
        debugLog?.log(`height #${key}: appear from ${startHeight}px`);
        // A transition runs from a previous computed style, and a brand-new element has none: without
        // resolving the start height first the browser only ever sees the final one and the item pops.
        void itemRef.offsetHeight;
        this.schedule(key, state);
        return true;
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
        this.drain(keys);
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
            state.outerExtra = -1;
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

    // Batched like a render: one callback carries every item a width change touched, and writing each
    // one as it is read would flush the layout the next read needs.
    private onContentResize = (entries: ResizeObserverEntry[]): void => {
        const wasBatching = this.isBatching;
        this.isBatching = true;
        try {
            for (const entry of entries) {
                const key = this.contentKeys.get(entry.target);
                const state = key == null ? undefined : this.states.get(key);
                if (key == null || state == null)
                    continue;

                const contentHeight = entry.borderBoxSize.length > 0
                    ? entry.borderBoxSize[0].blockSize
                    : entry.contentRect.height;
                // A resize is the one moment the chrome can change with no class changing - a width
                // change can move padding through a media query - so it is re-read, not trusted.
                state.outerExtra = -1;
                this.setIntrinsic(key, state, contentHeight + getOuterExtra(state));
            }
        }
        finally {
            if (!wasBatching)
                this.endBatch();
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

    // Every visibility read happens before the first write, and the whole batch shares one flush:
    // interleaving them costs a forced layout per item, which a width change makes O(rendered items).
    private drain(keys: string[]): void {
        // Top to bottom, so a batch of changes larger than the animation budget spends it on the items
        // nearest the top rather than on whichever one the observer happened to report first. Document
        // order is that order in both render directions - the container is a plain flex column, and
        // rendering in reverse anchors the chain differently without reordering it.
        keys.sort((a, b) => compareDomOrder(this.states.get(a)?.ref, this.states.get(b)?.ref));
        const pending = new Array<{ key: string, state: ItemHeightState, isVisible: boolean }>();
        for (const key of keys) {
            // The same guard schedule() applies: a key deferred while its height was known can be
            // recycled before the drain, and the state standing in its place has none yet. Writing
            // that would round -1 down to a 0px item.
            const state = this.states.get(key);
            if (state == null || state.intrinsic < 0)
                continue;

            pending.push({ key, state, isVisible: this.isVisible(key) });
        }
        if (pending.length === 0)
            return;

        const restore = new Array<HTMLElement>();
        this.pendingRestore = restore;
        try {
            for (const item of pending)
                this.scheduleNow(item.key, item.state, item.isVisible);
        }
        finally {
            this.pendingRestore = null;
            if (restore.length > 0) {
                void this.containerRef.offsetHeight;
                for (const itemRef of restore)
                    itemRef.style.transition = '';
            }
        }
    }

    private schedule(key: string, state: ItemHeightState): void {
        if (state.intrinsic < 0)
            return;

        if (this.isBatching)
            this.deferred.add(key);
        else
            this.scheduleNow(key, state);
    }

    private scheduleNow(key: string, state: ItemHeightState, isVisible?: boolean): void {
        const mustAnimate = !this.isSuspended
            && (isVisible ?? this.isVisible(key))
            && (state.isAppearing || (state.mustAnimateChanges && state.isControlled))
            && this.hasAnimationSlot(state);
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
        if (this.pendingRestore != null)
            this.pendingRestore.push(itemRef);
        else {
            void itemRef.offsetHeight;
            itemRef.style.transition = '';
        }

        return true;
    }

    // Whether this item may animate: either it already holds one of the budget's slots, or one is free.
    // Counting rather than tracking a number, because a slot is given up along a good half-dozen paths -
    // settling, being applied instantly, suspension, the item going away - and a counter that misses one
    // of them silently stops animating anything at all.
    private hasAnimationSlot(state: ItemHeightState): boolean {
        if (isAnimationPending(state))
            return true;

        let used = 0;
        for (const other of this.states.values()) {
            if (other === state || !isAnimationPending(other))
                continue;

            if (++used >= MaxAnimatedItems)
                return false;
        }

        return true;
    }

    // An appearance outranks a height change already in flight. A live conversation is never still -
    // its card and its newest transcript are both mid-transition whenever expand is pressed - so a
    // budget handed out first-come leaves the messages the expansion reveals to pop in while a line of
    // transcript finishes growing. The change that gives up its slot is the one nearest its end, so
    // what it gives up is the tail of a transition rather than the whole of one.
    private takeSlotFromChange(): boolean {
        const now = performance.now();
        let victim: ItemHeightState | null = null;
        let leastLeft = Number.POSITIVE_INFINITY;
        for (const state of this.states.values()) {
            if (state.isAppearing || !isAnimationPending(state))
                continue;

            // Nothing is on screen yet for an item still waiting out its settle delay, so snapping one
            // costs the whole change rather than its tail - taken only when nothing is transitioning.
            const left = state.animatingUntil > now ? state.animatingUntil - now : Number.MAX_SAFE_INTEGER;
            if (left >= leastLeft)
                continue;

            victim = state;
            leastLeft = left;
        }
        if (victim == null)
            return false;

        const chosen = victim;
        this.applyInstantly(state => state === chosen);
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

// Whether the item is using up one of the animation slots: waiting out its settle delay, transitioning,
// or parked at an appearance start height and not yet scheduled - the height it shows is not the height
// its content wants, and getting there is what the budget is for.
function isAnimationPending(state: ItemHeightState): boolean {
    return state.isAppearing || state.timer != null || isAnimating(state);
}

// Document order, which for these items is top to bottom. Deliberately not a position comparison: this
// runs mid-render, where every rect read is a forced layout.
function compareDomOrder(a: HTMLElement | undefined, b: HTMLElement | undefined): number {
    if (a == null || b == null || a === b)
        return 0;

    return (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0 ? -1 : 1;
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
    if (state.outerExtra >= 0)
        return state.outerExtra;

    const itemStyle = getComputedStyle(state.ref);
    const contentStyle = getComputedStyle(state.contentRef);
    state.outerExtra = (parseFloat(itemStyle.paddingTop) || 0)
        + (parseFloat(itemStyle.paddingBottom) || 0)
        + (parseFloat(itemStyle.borderTopWidth) || 0)
        + (parseFloat(itemStyle.borderBottomWidth) || 0)
        + (parseFloat(contentStyle.marginTop) || 0)
        + (parseFloat(contentStyle.marginBottom) || 0);
    return state.outerExtra;
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

// `data-vl-h-transition` names the only animation the item wants: "change" for an item that is always
// there and whose content grows into place, "appearance" for one whose height is the app's to write
// once it is on screen, "none" for neither. Absent, both run.
function mustAnimateChanges(transition: string | undefined): boolean {
    return transition !== 'appearance' && transition !== 'none';
}

function mustAnimateAppearance(transition: string | undefined): boolean {
    return transition !== 'change' && transition !== 'none';
}

function toWritableHeight(height: number): number | null {
    return height < 0 ? null : Math.max(0, Math.round(height));
}

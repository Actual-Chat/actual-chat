import { debounce, delayAsync, throttle } from 'actuallab-core';
import { DotNet } from '@microsoft/dotnet-js-interop';
import { getLogs } from 'logging';
import { clamp } from 'math';
import { DeviceInfo } from 'device-info';
import { fastRaf } from 'fast-raf';
import { ScrollController, ScrollLimits } from 'scroll-controller';
import { NumberRange, Range } from './ts/range';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListRenderDirection } from './ts/virtual-list-render-direction';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { diffKeys } from './ts/key-diff';
import { StabilityTracker } from './ts/stability-tracker';
import { ItemHeightController } from './ts/item-height-controller';
import { VirtualList } from './virtual-list';
import { isSpacerShown, VirtualListOverlayStats } from './virtual-list-overlay';

const { warnLog, debugLog } = getLogs('InfiniteList');

// Fixed scroll space of a scrollbar-less list: the loaded chain floats around its middle. At ~50px an
// item that is still ~40k items of scroll either way, and it clears every ceiling a browser puts on a
// length: Firefox's ~17.9M element height, and Chrome's 2^25 *physical* pixels, which is the tighter
// one because it scales with devicePixelRatio - 4M survives a ratio of 8.4, and phones today reach
// 3.75. Must match InfiniteSize in InfiniteList.razor.cs.
// Still only what we ask for, not necessarily what we get - see wrapperSize.
const InfiniteSize = 4e6;
const UpdateViewportIntervalMs = 64;
const UpdateVisibilityIntervalMs = 250;
const ScrollSettleMs = 200;
const ProgrammaticScrollGuardMs = DeviceInfo.isMobile ? 250 : 100;
const SmoothScrollMs = 500;
const EdgeEpsilon = 4;
const VisibilityEpsilon = 4;
const MinViewportSize = 400;
const SkeletonDetectionBoundaryPx = 200;
// How often a query that produced nothing is retried while a skeleton is still on screen.
const SkeletonRetryMs = 1000;
// Initial-reveal watch: how close the preferred edge must be to count as placed, and the backstop
// after which the wrapper is shown regardless (e.g. an empty chat that never "places").
const RevealEpsilon = 8;
const RevealTimeoutMs = 1500;
// An appearance animation on the initial population would grow a whole page out of nothing, so
// nothing animates until the list has been on screen this long.
const AppearanceQuietMs = 300;
// A freshly created list renders every item at once, and each one is "appearing" - without this they
// would all grow in from nothing on the first frame. The list is keyed by chat id, so this window is
// also the first moments of a chat view.
const InitialQuietMs = 500;
const RenderScriptPrefix = 'data-vl-render-script-';
// How long an item that leaves the render is remembered as having been on screen. A key that comes back
// inside this window did not appear - the source dropped it and put it back, which happens while a
// conversation block materializes around messages that were already there - and growing it from nothing
// is the one thing that must not happen to something the user was already reading. The window is what
// says which returns count: one the source never meant is effectively instant, a render or two apart,
// while anything the user asked for - collapsing a conversation and expanding it again - takes as long
// as reaching for the control twice, and is supposed to grow in like the first time.
const ReappearanceMs = 200;
// A re-pin re-derives its target from the DOM; when already flush that target sits ~1 device px off on
// fractional-DPI screens, and writing it flips the position by a pixel on every render.
const RepinEpsilon = 1;
// container.top moves with the scroll near an edge, so one nudge undershoots; three land sub-pixel.
const RepinMaxPasses = 3;
// How often a correction the list may not make yet asks again. A finger can rest on a pinned list for
// as long as it likes, and a pending animation frame makes the browser run its whole rendering
// lifecycle - retrying at frame rate would cost a style recalc per frame to re-read three booleans.
// 100ms is well inside the 200ms scroll hold it is usually waiting out.
const FollowRetryHz = 10;
// Consecutive frames an anchored element must not move before the list stops holding it. Long enough
// to bridge the gap between the model reaching its settled heights and the DOM getting there.
const ScreenAnchorStillFrames = 12;
// What the consumer marks a position:sticky element with, so the list can move its sticky threshold
// by whatever the rubber band's transform is carrying - see updateStickyItems.
const StickyItemClass = 'vl-sticky';
// Consecutive frames the position must not move before the list acts on a standing intent - shifting
// the chain back to the middle, or putting the render direction back. Event-based settle checks can't
// do this: a fling delivers no events between frames and still passes them, while the position itself
// cannot lie about whether the list is moving.
const QuietStillFrames = 3;
// How much of the room between the midpoint and each end of the scroll space has to stay free. Loading
// walks the chain towards one end, and at 20% of a 4M space that is 400k px of slack either way before
// it is shifted back - tens of thousands of messages, and the shift itself is free because it only
// happens at a standstill.
const RecentreReservePercent = 20;
// Leaving "the whole conversation fits on screen" costs a jump of the end anchor's whole height, so a
// chain hovering at exactly one viewport - a transcript growing and shrinking by a line - must not
// toggle it on every measure.
const ChainFittingExitPx = 64;
const InteractiveAnchorTtlMs = 2000;
// A WASM render can arrive several seconds after its click; the screen anchor has to survive until
// that replacement DOM exists, while its shorter active hold starts only once placement begins.
const ScreenAnchorRenderTtlMs = 10_000;
// Overscrolling past the loaded chain is legitimate - it is how reading further back starts, and the
// window follows. This is only where it stops being a scroll and starts being a fall: far enough out
// that the query built from there asks for a window the data can't reach, so nothing would ever come
// to meet the view.
const MaxOverscrollScreens = 3;
// Past twice the allowance the blank is not something scrolling can produce - the view and its chain
// have come apart, and only a re-pin brings them back.
const StrandedGapFactor = 2;
const DriftWarnThresholdPx = 8;
const ContentOverflowThresholdPx = 2;
const DefaultItemHeight = 48;

// The pinned edge's own place in the diffed key sequences. A symbol, so no item key can collide with
// it: present in both sequences, something appended while the list is parked at that edge merges
// before it - i.e. inside the old range - and therefore animates.
const EdgeSentinel = Symbol('virtualList.edge');
type ItemKey = string | symbol;

// A sticky element the consumer has declared, the insets its own stylesheet gives it - null for an
// inset it does not use - and whether those are currently carrying the shift.
interface StickyItem {
    readonly ref: HTMLElement;
    top: number | null;
    bottom: number | null;
    isShifted: boolean;
}

interface InfiniteListItem {
    readonly key: string;
    ref: HTMLElement;
    height: number;
    mustSkipKey: boolean;
}

// An edge pin is a standing constraint - "stay flush with this end" - so it is applied at once and
// again once the list settles, and never blocks an animation. A jump is a one-off intent whose target
// depends on where the content will end up, so it suppresses new animations, waits out the ones in
// flight, and only then reads the geometry it needs.
type RepositionTarget =
    | { kind: 'edge'; edge: VirtualListEdge }
    | { kind: 'key'; key: string; position: ScrollLogicalPosition };

interface Jump {
    readonly target: RepositionTarget;
    readonly isSmooth: boolean;
    readonly priority: number;
    readonly reason: string;
}

const JumpPriority = {
    stranded: 1,
    navigation: 2,
};

// A virtualized list of unbounded length: no scrollbar, a fixed huge virtual scroll space, and an
// absolutely positioned chain of loaded items floating in it. Every position is modelled in wrapper
// coordinates from measured intrinsic item heights, so holding an item's screen position across a
// render is one subtraction rather than a re-measurement.
// Runs when `data-vl-render-script-<name>` appears on an item or anything nested in one, or its value
// changes. Unlike the MutationProcessor equivalent this runs inside the list's own render pass, before
// appearances are decided - which is the only point early enough to still change what they do.
export type VirtualListRenderScript = (list: InfiniteList, element: HTMLElement, value: string) => void;

export class InfiniteList extends VirtualList {
    private static readonly renderScripts = new Map<string, VirtualListRenderScript>();
    // What each element was last run with, so a re-render that rewrites the same value is not a re-run.
    private readonly ranRenderScripts = new WeakMap<Element, Map<string, string>>();

    public static isDebugEnabled = false;
    private static readonly instances = new Set<InfiniteList>();
    private static readonly pageLockOwners = new Set<InfiniteList>();
    private static pageLockSnapshot: {
        htmlPosition: string,
        htmlOverflowX: string,
        bodyPosition: string,
        bodyOverflowX: string,
    } | null = null;

    public static setDebugEnabled(isEnabled: boolean): void {
        InfiniteList.isDebugEnabled = isEnabled;
        for (const instance of InfiniteList.instances)
            instance.checkModelDrift('setDebugEnabled');
    }

    private readonly endAnchorRef: HTMLElement;
    private readonly scrollController: ScrollController;
    private readonly sizeObserver: ResizeObserver;
    private readonly visibilityObserver: IntersectionObserver;
    private readonly skeletonObserver: IntersectionObserver;
    private readonly stability = new StabilityTracker();
    private readonly heights: ItemHeightController | null;
    private readonly visibleKeys = new Set<string>();

    private items = new Array<InfiniteListItem>();
    private indexByKey = new Map<string, number>();
    // offsets[i] is the distance from the chain's top to item i's top; offsets[n] is one row gap past
    // the chain's bottom.
    private offsets = [0];
    private chainStart = Math.round(InfiniteSize / 2);
    // What the browser actually gave the wrapper. Chrome keeps layout coordinates in 1/64 of a physical
    // pixel in a 32-bit int, so a length past 2^25 physical pixels is silently clamped - at
    // devicePixelRatio 3.75 a 10M-px wrapper comes back 8,947,847. In reverse every coordinate is
    // measured from the wrapper's bottom edge, so believing the number we asked for placed the chain a
    // megapixel from where the scroll position said it was, on that device only.
    private wrapperSize = InfiniteSize;
    // The direction in force right now, which is the configured one except while an anchored element
    // is being held - see onInteractiveEvent.
    private readonly isReverse: boolean;
    private pinnedEdge: VirtualListEdge | null = null;
    private endAnchorSize = 0;
    private lastProgrammaticScrollAt = 0;
    private isWatchingStillness = false;
    private isAwaitingOverscrollEnd = false;
    private isNearSkeleton = false;
    private isStartSkeletonShown = false;
    private isEndSkeletonShown = false;
    private revealedAt = 0;
    private interactiveAnchor: { key: string; at: number } | null = null;
    // An element the caller has promised to render again under the same id, whose position on screen
    // must survive the next render. Recorded as rendered rather than modelled, which is the whole
    // point of it: a stuck sticky element is not where the model puts it, and an element inside a
    // group has no modelled position at all.
    private screenAnchor:
        { id: string; top: number; at: number; isPlaced: boolean; corrected: number } | null = null;
    private isWatchingScreenAnchor = false;
    private stickyShift = 0;
    // Keys that left a recent render, with the height they had, so an item that comes straight back is
    // recognised as one that never went away - see applyAppearances.
    private recentlyRemoved = new Map<string, { height: number; at: number }>();
    private stickyRefs = new Array<StickyItem>();
    // The pinned edge's follow, which is a scroll write on a frame rather than a translation: whether
    // one is already booked for this frame, and where the last one landed, so onScroll can tell that
    // echo from the user.
    private isFollowScheduled = false;
    private isFollowDeferred = false;
    private lastFollowTop: number | null = null;
    private pendingJump: Jump | null = null;
    private isAwaitingStability = false;
    private isAwaitingJump = false;
    private isInitiallyPlaced = false;
    private isApplyingRender = false;
    private isChainWithinViewport = false;
    private mustRecentre = false;
    private lastWrapperSize = 0;
    private handledScrollToKey: string | null = null;

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
        defaultEdge: VirtualListEdge,
        renderDirection: VirtualListRenderDirection,
        mustAnimateItemHeight: boolean,
        spacerSize: number,
        expandMultiplier: number,
        retainedItemCount = 5,
    ): InfiniteList {
        return new InfiniteList(
            ref,
            backendRef,
            identity,
            defaultEdge,
            renderDirection,
            mustAnimateItemHeight,
            spacerSize,
            expandMultiplier,
            retainedItemCount);
    }

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
        private readonly defaultEdge: VirtualListEdge,
        private readonly renderDirection: VirtualListRenderDirection,
        mustAnimateItemHeight: boolean,
        private readonly spacerSize: number,
        private readonly expandMultiplier: number,
        private readonly retainedItemCount = 5,
    ) {
        super(ref, backendRef, identity);
        this.endAnchorRef = this.containerRef.querySelector(':scope > .c-end-anchor')!;
        this.endAnchorSize = this.endAnchorRef.getBoundingClientRect().height;
        this.lastWrapperSize = InfiniteSize;
        this.wrapperRef.style.height = `${InfiniteSize}px`;
        this.wrapperSize = this.wrapperRef.offsetHeight || InfiniteSize;
        this.chainStart = Math.round(this.wrapperSize / 2);
        this.isReverse = renderDirection === VirtualListRenderDirection.Reverse;
        ref.style.flexDirection = this.isReverse ? 'column-reverse' : 'column';
        this.heights = mustAnimateItemHeight
            ? new ItemHeightController(
                this.containerRef,
                this.stability,
                key => this.isKeyOnScreen(key),
                key => this.onItemHeightChanged(key))
            : null;
        this.heights?.suspendUntil(delayAsync(InitialQuietMs));
        this.scrollController = new ScrollController(
            ref, true, this.containerRef, () => this.computeScrollLimits());
        this.scrollController.onTransform = () => this.updateStickyItems();
        const listenerOptions = { passive: true, signal: this.abortController.signal };
        ref.addEventListener('scroll', this.onScroll, listenerOptions);
        ref.addEventListener('scrollend', this.onScrollEnd, listenerOptions);
        ref.addEventListener('wheel', this.onWheel, listenerOptions);
        // Visibility is reported with an "is the user looking at the newest item" flag that is forced
        // off while the tab is hidden, so coming back has to re-report it.
        document.addEventListener('visibilitychange', this.onDocumentVisibilityChange, listenerOptions);
        // On the document because a touch listener on this container costs WebKit a walk of the whole
        // transcript per rendering update (6-12% of its main thread, measured during a call), and in
        // the capture phase so a control that stops the event cannot hide it from us. The handler
        // resolves everything from event.target, so it only has to test containment itself.
        const captureOptions = { signal: this.abortController.signal, passive: true, capture: true };
        document.addEventListener('click', this.onInteractiveEvent, captureOptions);
        document.addEventListener('touchend', this.onInteractiveEvent, captureOptions);

        this.sizeObserver = new ResizeObserver(this.onResize);
        this.sizeObserver.observe(ref, { box: 'border-box' });
        this.sizeObserver.observe(this.endAnchorRef, { box: 'border-box' });

        const thresholds = [...Array(11).keys()].map(i => i / 10);
        this.visibilityObserver = new IntersectionObserver(this.onItemVisibilityChanged, {
            root: ref,
            rootMargin: `${VisibilityEpsilon}px`,
            threshold: thresholds,
        });
        this.skeletonObserver = new IntersectionObserver(this.onSkeletonVisibilityChanged, {
            root: ref,
            rootMargin: `${SkeletonDetectionBoundaryPx}px`,
            threshold: [0, 0.1],
        });
        this.skeletonObserver.observe(this.spacerRef);
        this.skeletonObserver.observe(this.endSpacerRef);

        InfiniteList.instances.add(this);
        (globalThis as unknown as { InfiniteList: typeof InfiniteList }).InfiniteList = InfiniteList;
        this.start();
        this.startRevealWatch();
    }

    public override dispose(): void {
        super.dispose();
        InfiniteList.setPageLock(this, false);
        InfiniteList.instances.delete(this);
        this.stability.dispose();
        this.heights?.dispose();
        this.scrollController.dispose();
        this.sizeObserver.disconnect();
        this.visibilityObserver.disconnect();
        this.skeletonObserver.disconnect();
    }

    // Blocks item height animations until `whenDone` settles - see ItemHeightController.suspendUntil.
    // Calls stack, so a caller can hold the block open without having to know who else is holding it.
    public suspendHeightAnimationsUntil(whenDone: PromiseLike<unknown>): void {
        this.heights?.suspendUntil(whenDone);
    }

    public static registerRenderScript(name: string, script: VirtualListRenderScript): void {
        InfiniteList.renderScripts.set(name, script);
    }

    public getOverlayStats(): VirtualListOverlayStats {
        const rs = this.renderState;
        return {
            renderDirection: this.isReverse ? 'up' : 'down',
            stickyEdge: this.pinnedEdge == null
                ? null
                : this.pinnedEdge === VirtualListEdge.End ? 'down' : 'up',
            hasStartSpacer: isSpacerShown(this.spacerRef),
            hasEndSpacer: isSpacerShown(this.endSpacerRef),
            hasVeryFirstItem: rs.hasVeryFirstItem,
            hasVeryLastItem: rs.hasVeryLastItem,
            isRequestingData: this.isRequestingData,
            isRendering: this.stability.isAnimating,
            lastDataRequestAt: this.lastDataRequestAt,
            lastRenderAt: this.lastRenderAt,
            window: `${this.visibleKeys.size}/${this.items.length}`,
            total: null,
            meanItemHeight: this.meanItemHeight,
        };
    }

    // Protected methods

    protected onRender(rs: VirtualListRenderState): void {
        this.isApplyingRender = true;
        this.heights?.beginBatch();
        try {
            this.applyRender(rs);
        }
        finally {
            this.isApplyingRender = false;
            this.heights?.endBatch();
        }
        this.updateVisibilityThrottled();
        this.updateViewportThrottled();
        this.checkModelDrift('render');
    }

    protected buildDataQuery(): VirtualListDataQuery | null {
        const rs = this.renderState;
        if (rs.hasVeryFirstItem && rs.hasVeryLastItem)
            return null;
        if (this.items.length === 0 || this.isRequestingData)
            return null;

        const clientHeight = this.ref.clientHeight;
        if (clientHeight <= 0)
            return null;

        const contentItems = this.items.filter(x => !x.mustSkipKey);
        if (contentItems.length === 0)
            return null;

        const scrollOffset = this.scrollOffset;
        const viewport = new NumberRange(scrollOffset, scrollOffset + clientHeight);
        const loaded = new NumberRange(this.chainStart, this.chainEnd);
        const zone = viewport.size * this.expandMultiplier;
        // Clamped one way only at a known edge: there is nothing further out to ask for, but the zone
        // moving inwards still has to be able to drop what it has left behind. Clamping both ways makes
        // every query at that edge extend-only, and a long read through history then ends up holding
        // thousands of items.
        let loadStart = rs.hasVeryFirstItem
            ? Math.max(viewport.start - zone, loaded.start)
            : viewport.start - zone;
        let loadEnd = rs.hasVeryLastItem
            ? Math.min(viewport.end + zone, loaded.end)
            : viewport.end + zone;
        // Anything on screen has to stay loaded, whatever the zone says: unloading a visible item would
        // drop the anchor the next render holds the view by.
        const retained = this.getRetainedRange(viewport);
        if (retained != null) {
            loadStart = Math.min(loadStart, retained.start);
            loadEnd = Math.max(loadEnd, retained.end);
        }
        const loadZone = new NumberRange(loadStart, loadEnd);
        if (loaded.contains(loadZone))
            return null;

        const itemSize = this.meanItemHeight;
        const firstItem = this.firstItemEndingAfter(contentItems, loadZone.start);
        const lastItem = this.lastItemStartingBefore(contentItems, loadZone.end);
        const firstTop = this.topOf(firstItem);
        const lastBottom = this.topOf(lastItem) + lastItem.height;
        const startGap = Math.max(0, firstTop - loadZone.start);
        const endGap = Math.max(0, loadZone.end - lastBottom);
        // Loading a handful of extra items costs a full render, so wait until the gap is worth it -
        // unless a skeleton is on screen, in which case the user is already looking at the hole.
        if (!this.isNearSkeleton && startGap < viewport.size / 2 && endGap < viewport.size / 2)
            return null;

        // Rounded to 5, so a slowly drifting viewport doesn't produce a new query on every frame.
        const moveStart = Math.floor((loadZone.start - firstTop) / itemSize / 5) * 5;
        const moveEnd = Math.ceil((loadZone.end - lastBottom) / itemSize / 5) * 5;
        if (moveStart === 0 && moveEnd === 0)
            return null;

        const query = new VirtualListDataQuery(
            new Range<string>(firstItem.key, lastItem.key),
            loadZone,
            new NumberRange(moveStart, moveEnd));
        query.expectedCount = Math.ceil(loadZone.size / itemSize);
        // Repeating a query is normally pointless, but a visible skeleton means the user is looking at
        // a hole the last attempt didn't fill - so that one is retried, slowly.
        const canRetry = this.isNearSkeleton
            && performance.now() - (this.lastDataRequestAt ?? 0) > SkeletonRetryMs;
        if (this.lastSentQuery != null && isSameQuery(query, this.lastSentQuery) && !canRetry)
            return null;

        return query;
    }

    // Deliberately doesn't re-derive the pinned edge: revealing can happen on a timeout, before the
    // content has finished settling, and re-deriving there would drop the pin the initial placement just
    // established - leaving a freshly opened chat at the bottom but not following it.
    protected override reveal(): void {
        const wasRevealed = this.isContainerRevealed;
        super.reveal();
        if (wasRevealed)
            return;

        this.revealedAt = performance.now();
    }

    // Private methods

    private applyRender(rs: VirtualListRenderState): void {
        const oldKeys = this.items.map(x => x.key);
        const oldOffsets = this.offsets;
        const oldChainStart = this.chainStart;
        const oldHeights = new Map(this.items.map(x => [x.key, x.height]));

        this.rebuildItems();
        this.runRenderScripts();
        const isFullReplacement = oldKeys.length > 0
            && this.items.length > 0
            && !oldKeys.some(key => this.indexByKey.has(key));
        if (isFullReplacement) {
            // Nothing on screen survives, so no animation can be interrupted and nothing needs holding
            // in place: whatever was animating is gone, and the list is stable by construction.
            this.heights?.applyAllInstantly();
            this.stability.releaseAllAnimations();
        }
        if (!sameKeys(oldKeys, this.items))
            this.lastSentQuery = null;

        this.findStickyItems();
        this.measureItems();
        this.computeOffsets();
        if (!isFullReplacement)
            this.reanchor(oldKeys, oldOffsets, oldChainStart);

        if (this.applyAppearances(rs, oldKeys, oldHeights, isFullReplacement)) {
            const settledOffsets = this.offsets;
            const settledChainStart = this.chainStart;
            this.computeOffsets();
            this.reanchor(this.items.map(x => x.key), settledOffsets, settledChainStart);
        }
        this.applyLayout('render');
        this.applyRenderIntent(rs);
    }

    private get chainSize(): number {
        return this.items.length === 0 ? 0 : this.offsets[this.items.length] - this.rowGap;
    }

    private get chainEnd(): number {
        return this.chainStart + this.chainSize;
    }

    private get meanItemHeight(): number {
        let sum = 0;
        let count = 0;
        for (const item of this.items) {
            if (item.height <= 0)
                continue;

            sum += item.height;
            count++;
        }
        return count === 0 ? DefaultItemHeight : sum / count;
    }

    private get firstItemRef(): HTMLElement | null {
        return this.items.length > 0 ? this.items[0].ref : null;
    }

    private get lastItemRef(): HTMLElement | null {
        return this.items.length > 0 ? this.items[this.items.length - 1].ref : null;
    }

    // Wrapper coordinates: the viewport's top within the fixed scroll space, always in [0, maxScrollTop]
    // whichever direction is live. These four plus applyLayout are the only members that know reverse
    // puts the scroll origin at the bottom, making native scrollTop run from -maxScrollTop to 0.
    private get maxScrollTop(): number {
        return Math.max(0, this.ref.scrollHeight - this.ref.clientHeight);
    }

    // How far past the loaded chain the view may travel while the window catches up.
    private get maxOverscroll(): number {
        return this.ref.clientHeight * MaxOverscrollScreens;
    }

    private get scrollOffset(): number {
        return this.isReverse ? this.ref.scrollTop + this.maxScrollTop : this.ref.scrollTop;
    }

    private toScrollTop(scrollOffset: number): number {
        return this.isReverse ? scrollOffset - this.maxScrollTop : scrollOffset;
    }

    // A jump to an absolute position, and the only thing that writes scrollTop outside a follow.
    private setScrollOffset(scrollOffset: number, isSmooth = false, mustClamp = true, isReanchor = false): void {
        this.lastProgrammaticScrollAt = performance.now();
        if (isSmooth)
            this.stability.holdScroll(SmoothScrollMs);
        this.scrollController.scrollTo(this.toScrollTop(scrollOffset),
            { smooth: isSmooth, clamp: mustClamp, reanchor: isReanchor });
    }

    private topOf(item: InfiniteListItem): number {
        const index = this.indexByKey.get(item.key);
        return index == null ? this.chainStart : this.chainStart + this.offsets[index];
    }

    private rebuildItems(): void {
        const itemRefs = this.containerRef.querySelectorAll<HTMLElement>(':scope .item[data-key]');
        const items = new Array<InfiniteListItem>(itemRefs.length);
        const indexByKey = new Map<string, number>();
        for (let i = 0; i < itemRefs.length; i++) {
            const itemRef = itemRefs[i];
            const key = itemRef.dataset.key!;
            const oldIndex = this.indexByKey.get(key);
            const existing = oldIndex == null ? null : this.items[oldIndex];
            const item: InfiniteListItem = existing ?? {
                key,
                ref: itemRef,
                height: -1,
                mustSkipKey: false,
            };
            if (existing == null)
                this.observeItem(itemRef);
            else if (existing.ref !== itemRef) {
                this.unobserveItem(existing.ref);
                existing.ref = itemRef;
                this.observeItem(itemRef);
            }
            // Blazor renders a true bool attribute as a valueless one (data-skip=""), never "true".
            item.mustSkipKey = itemRef.dataset.skip != null && itemRef.dataset.skip !== 'false';
            items[i] = item;
            indexByKey.set(key, i);
            this.heights?.track(key, itemRef);
        }

        const now = performance.now();
        for (const item of this.items) {
            if (indexByKey.has(item.key))
                continue;

            this.recentlyRemoved.set(item.key, { height: item.height, at: now });
            this.unobserveItem(item.ref);
            this.visibleKeys.delete(item.key);
            this.heights?.untrack(item.key);
        }
        // Aged out, and only aged out: the render that brings a key back runs this before the appearances
        // are decided, so forgetting it here for being present again is forgetting it exactly when it
        // was about to be needed.
        for (const [key, removed] of this.recentlyRemoved)
            if (now - removed.at > ReappearanceMs)
                this.recentlyRemoved.delete(key);
        this.items = items;
        this.indexByKey = indexByKey;
    }

    private observeItem(itemRef: HTMLElement): void {
        this.visibilityObserver.observe(itemRef);
        // A controlled item's own box is whatever we last wrote to it, so it can say nothing about what
        // the content wants - the height controller watches the content element instead.
        if (this.heights == null)
            this.sizeObserver.observe(itemRef, { box: 'border-box' });
    }

    private unobserveItem(itemRef: HTMLElement): void {
        this.visibilityObserver.unobserve(itemRef);
        if (this.heights == null)
            this.sizeObserver.unobserve(itemRef);
    }

    private measureItems(): void {
        let fallback = -1;
        for (const item of this.items) {
            const height = this.heights?.remeasure(item.key) ?? item.ref.getBoundingClientRect().height;
            if (height > 0 || isLaidOut(item.ref)) {
                item.height = height;
                continue;
            }
            if (item.height >= 0)
                continue;

            if (fallback < 0)
                fallback = this.meanItemHeight;
            item.height = fallback;
        }
    }

    private computeOffsets(): void {
        const offsets = new Array<number>(this.items.length + 1);
        let offset = 0;
        for (let i = 0; i < this.items.length; i++) {
            offsets[i] = offset;
            offset += Math.max(0, this.items[i].height) + this.rowGap;
        }
        offsets[this.items.length] = offset;
        this.offsets = offsets;
    }

    // Holds one item's wrapper coordinate fixed across a re-layout, which is what keeps the view put:
    // every position is chainStart + offset, so placing the whole chain is a single subtraction.
    private reanchor(oldKeys: string[], oldOffsets: number[], oldChainStart: number): boolean {
        if (oldKeys.length === 0 || this.items.length === 0)
            return false;

        if (this.applyScreenAnchor())
            return true;

        const viewportTop = this.scrollOffset - oldChainStart;
        const interactiveKey = this.getFreshInteractiveAnchorKey();
        if (interactiveKey != null) {
            const oldIndex = oldKeys.indexOf(interactiveKey);
            const newIndex = this.indexByKey.get(interactiveKey);
            if (oldIndex >= 0 && newIndex != null) {
                // Holding the clicked item's top is the contract, but it only places the view while the
                // view is inside that item. Collapsing the conversation you are reading is the case
                // where it isn't: the control sits at the top of a block the viewport is thousands of
                // pixels into, and the block becomes a single row - so the offset that used to land
                // inside it now lands far past it, on whatever the collapse pulled up. Then the item
                // itself is what the user gets back.
                const offset = viewportTop - oldOffsets[oldIndex];
                const extent = this.offsets[newIndex + 1] - this.offsets[newIndex];
                this.chainStart = oldChainStart + oldOffsets[oldIndex] - this.offsets[newIndex]
                    + (offset > extent ? offset : 0);
                return true;
            }
        }

        // The item at the viewport top: everything the user is reading sits below it, so holding it
        // still means a change further down grows away from them rather than under them. When that item
        // is gone, the pair of surviving items that bracketed it is what the view is held between.
        const start = lowerBound(oldOffsets, oldKeys.length, viewportTop);
        let before = -1;
        for (let i = start; i >= 0 && before < 0; i--)
            if (this.indexByKey.has(oldKeys[i]))
                before = i;

        let after = -1;
        for (let i = start + 1; i < oldKeys.length && after < 0; i++)
            if (this.indexByKey.has(oldKeys[i]))
                after = i;

        if (before < 0) {
            if (after < 0)
                return false;

            // Nothing above the viewport survived, so there is no offset into anything to keep: the
            // first thing that did survive goes to the top.
            this.chainStart = oldChainStart + viewportTop - this.offsets[this.indexByKey.get(oldKeys[after])!];
            return true;
        }

        const newBefore = this.indexByKey.get(oldKeys[before])!;
        const offset = viewportTop - oldOffsets[before];
        // What the two survivors have room for between them now. Collapsing the conversation you are
        // reading takes every key at the viewport with it, and the gap that held them shrinks to a
        // single row - so keeping the raw offset into that gap drops the view clean past the row, onto
        // whatever happens to sit that far below. Landing at the top of the gap puts the collapsed
        // block where the user was already looking.
        const gap = after < 0
            ? Number.POSITIVE_INFINITY
            : this.offsets[this.indexByKey.get(oldKeys[after])!] - this.offsets[newBefore];
        this.chainStart = oldChainStart + oldOffsets[before] - this.offsets[newBefore]
            + (offset > gap ? offset : 0);
        return true;
    }

    // Whether the chain has eaten into the reserve at either end of the scroll space.
    private isChainOffCentre(): boolean {
        if (this.items.length === 0)
            return false;

        const reserve = (this.wrapperSize / 2) * (RecentreReservePercent / 100);
        return this.chainStart < reserve || this.chainEnd > this.wrapperSize - reserve;
    }

    private applyLayout(reason: string): void {
        this.updateChainFitting();
        // Re-read every time rather than trusted from construction: the clamp depends on
        // devicePixelRatio, which changes when the window moves to another display or the page is
        // zoomed, and a stale value here is a chain drawn nowhere near its scroll position.
        const wrapperSize = this.wrapperRef.offsetHeight || this.wrapperSize;
        this.wrapperSize = wrapperSize;
        // The chain drifts towards one end as loading extends it, and is shifted back to the middle as a
        // rigid body with the scroll following it. That is a coordinate change like a direction flip -
        // invisible at a standstill, a jump anywhere else - so it is decided here and performed on the
        // same gate, never inline on whatever render happened to notice.
        let scrollShift = 0;
        if (this.mustRecentre) {
            this.mustRecentre = false;
            const target = Math.round((wrapperSize - this.chainSize) / 2);
            scrollShift = target - this.chainStart;
            this.chainStart = target;
            debugLog?.log(`[${this.identity}] applyLayout: re-centred by ${scrollShift} (${reason})`);
        }
        else if (this.isChainOffCentre())
            this.watchQuietMoment();

        setSpacerSize(this.spacerRef, this.startSpacerSize);
        setSpacerSize(this.endSpacerRef, this.endSpacerSize);
        // One size, for the life of the list, in either direction. Trimming it to the newest item used
        // to give the bottom a native hard stop - but it also made the bottom boundary the scroller's
        // own end, where the overscroll pull can never engage, so the two edges resisted differently.
        // And in reverse the scroll origin is that same edge, so every trim moved maxScrollTop and had
        // to be paid for with either a jump or a scrollTop write that ends a fling. Constant, both
        // boundaries sit inside the scroll range and are enforced the same way by the same code.
        if (this.lastWrapperSize !== InfiniteSize) {
            this.lastWrapperSize = InfiniteSize;
            this.wrapperRef.style.height = `${InfiniteSize}px`;
        }

        this.writeChainPosition();
        // Here rather than only on the next frame: this is the write that moves the chain, and a
        // correction that waits for the next frame lets the whole jump paint once - measured at 349px
        // for one frame when a block was expanded.
        if (this.isWatchingScreenAnchor && this.canCorrectPosition)
            this.correctScreenAnchor();
        // Flagged as a re-anchor: this is the window moving under a still view, so an overscroll return
        // in flight must be carried across it, not cancelled - the data that caused it usually arrives
        // mid-bounce, because reaching the edge is what asked for it. The position is re-read here
        // rather than before the writes above, because the anchor correction is one of them.
        if (scrollShift !== 0)
            this.setScrollOffset(this.scrollOffset + scrollShift, false, false, true);
        // Same reason as the anchor above: while the list follows an edge, the chain write moves it by
        // everything this render added, and the scroll that compensates is what keeps the view still.
        // Left to the frame the follow is scheduled on, that displacement paints once - measured at
        // 200-300px per render against a live conversation, on Chrome and on Android. The scheduled
        // follow stays as the fallback for the frames this one may not write.
        if (this.pinnedEdge != null && this.canCorrectPosition)
            this.applyFollow(this.measureFollow(), 'layout');
        // Clamping needs the real sizes, and mid-animation the DOM does not have them yet; onceStable
        // re-runs it with the content where it will actually be.
        // Skipped entirely while pinned, because a pinned list is about to correct itself by re-pinning
        // and the clamp would get there first with a scroll write - which is the one thing the re-pin
        // exists to avoid. The re-pin lands inside the band by construction, and the settled pass
        // clamps behind it either way.
        if (!this.stability.isAnimating && this.pinnedEdge == null)
            this.scrollController.clampToLimits();
    }

    // Sticky elements are clamped during layout, against the real scroll position, and the band's
    // transform is applied after that - so a stuck one is carried by the band instead of staying where
    // it is stuck. Measured here: a 120px transform moved every stuck element by the whole 120px.
    //
    // What moves is the threshold, not the element. `top: base + shift` puts the clamp where the
    // transform is about to take it, so the browser paints what the same amount of real scrolling would
    // have painted - measured at 0.00px against a real scroll of ±120px over every element on screen,
    // against 27px and 109px for the transform alone, and at 0.25px against 66.25px under a live band.
    // Which elements are pinned never comes into it, and that is the point: three different tests for
    // pinnedness were all wrong, and the browser needs none.
    private updateStickyItems(): void {
        const shift = -this.scrollController.bandOffset;
        if (shift === this.stickyShift)
            return;

        this.stickyShift = shift;
        for (const sticky of this.stickyRefs)
            this.writeStickyInsets(sticky);
    }

    // The element's own insets are read the first time it has to carry a shift and dropped when there
    // is none left to carry, so what is read is always its stylesheet rather than a value written here:
    // a media query or a class may have changed the inset between one excursion and the next.
    private writeStickyInsets(sticky: StickyItem): void {
        const shift = this.stickyShift;
        const style = sticky.ref.style;
        if (shift === 0) {
            if (!sticky.isShifted)
                return;

            sticky.isShifted = false;
            style.top = '';
            style.bottom = '';
            return;
        }

        if (!sticky.isShifted) {
            if (!sticky.ref.isConnected)
                return;

            const computed = getComputedStyle(sticky.ref);
            sticky.top = readInset(computed.top);
            sticky.bottom = readInset(computed.bottom);
            sticky.isShifted = true;
        }
        if (sticky.top != null)
            style.top = `${sticky.top + shift}px`;
        if (sticky.bottom != null)
            style.bottom = `${sticky.bottom - shift}px`;
    }

    // Declared by the consumer, with StickyItemClass, rather than discovered: finding them would mean
    // a computed style for every descendant. Once per render, since the set only changes when the
    // rendered items do.
    private findStickyItems(): void {
        const known = new Map(this.stickyRefs.map(x => [x.ref, x]));
        const refs = new Array<StickyItem>();
        for (const ref of this.containerRef.querySelectorAll<HTMLElement>(`:scope .${StickyItemClass}`)) {
            const sticky = known.get(ref) ?? { ref, top: null, bottom: null, isShifted: false };
            // A render can land in the middle of an excursion, and an element that arrives during one
            // has to be given the shift the others are already carrying.
            this.writeStickyInsets(sticky);
            refs.push(sticky);
            known.delete(ref);
        }
        this.stickyRefs = refs;
        // Whatever stopped being sticky would keep the insets it was given: nothing writes them again.
        for (const sticky of known.values()) {
            if (!sticky.isShifted)
                continue;

            sticky.isShifted = false;
            sticky.ref.style.top = '';
            sticky.ref.style.bottom = '';
        }
    }

    // Where the chain sits in the fixed scroll space, and the only writer of it. The spacer stands
    // between the container's edge and the first item, so it comes out of the same number.
    private writeChainPosition(): void {
        // Which end is anchored is which end has to stay put. Following the newest content, that is the
        // bottom, taken from the model rather than from offsetHeight: the model carries settled heights,
        // so what is flush with the fold stays flush while an item animates instead of drifting with
        // what the DOM has managed to render so far.
        //
        // Reading above the end it is the reader, i.e. the top - and a bottom-anchored container hangs
        // its top edge lower by exactly what the render is still behind by, which drags everything above
        // an animating item down and back as the animation lands. Measured: a 40px growth below the
        // reader moved them 40px, then 33, 24, 16, 7, 0. Anchoring the top instead is the same placement
        // arithmetic seen from the other end - a chain grows downward from a fixed top - so the browser
        // holds it every frame with nothing to compute, and the two agree wherever nothing is animating.
        if (!this.isReverse || this.pinnedEdge == null) {
            this.containerRef.style.bottom = '';
            this.containerRef.style.top = `${this.chainStart - this.startSpacerSize}px`;
        }
        else {
            this.containerRef.style.top = '';
            this.containerRef.style.bottom =
                `${this.wrapperSize - this.chainEnd - this.endSpacerSize - this.endAnchorSize}px`;
        }
    }

    private get startSpacerSize(): number {
        return this.renderState.hasVeryFirstItem
            ? 0
            : clamp(this.chainStart, 0, this.spacerSize);
    }

    private get endSpacerSize(): number {
        return this.renderState.hasVeryLastItem ? 0 : this.spacerSize;
    }

    // Whether the whole conversation is loaded and short enough to show at once - both edges on screen
    // together, so the view has one correct resting place with the first item at the top.
    private updateChainFitting(): void {
        const rs = this.renderState;
        if (!rs.hasVeryFirstItem || !rs.hasVeryLastItem || this.items.length === 0) {
            this.isChainWithinViewport = false;
            return;
        }

        const limit = this.ref.clientHeight;
        this.isChainWithinViewport =
            this.chainSize <= (this.isChainWithinViewport ? limit + ChainFittingExitPx : limit);
    }

    private applyRenderIntent(rs: VirtualListRenderState): void {
        const scrollToKey = rs.scrollToKey;
        if (scrollToKey != null && this.indexByKey.has(scrollToKey)) {
            this.isInitiallyPlaced = true;
            if (scrollToKey === this.getLastContentKey() && rs.hasVeryLastItem) {
                this.handledScrollToKey = scrollToKey;
                this.setPinnedEdge(VirtualListEdge.End);
                this.repinEdge('scroll-to-last');
                this.repinWhenStable();
                return;
            }
            if (this.getPendingJumpKey(rs) != null) {
                this.handledScrollToKey = scrollToKey;
                this.setPinnedEdge(null);
                this.requestJump({
                    target: {
                        kind: 'key',
                        key: scrollToKey,
                        position: rs.scrollToKeyInTheMiddle ? 'center' : 'end',
                    },
                    isSmooth: false,
                    priority: JumpPriority.navigation,
                    reason: 'scroll-to-key',
                });
                return;
            }
        }
        else
            this.handledScrollToKey = null;

        // An interactive anchor means the user just clicked something inside an item, and that item's
        // screen position is what this render has to preserve - reanchor already did exactly that, so an
        // edge re-pin here would only drag the clicked row towards the edge.
        if (this.getFreshInteractiveAnchorKey() != null || this.hasFreshScreenAnchor())
            return;

        if (this.pinnedEdge != null) {
            this.repinEdge('render');
            this.repinWhenStable();
            return;
        }

        // The first render that puts anything on screen decides where the list opens. Keyed off having
        // placed it rather than off the render index: the JS side is created after Blazor's first
        // render, and a chat switch resets the index without resetting anything the user can see.
        if (!this.isInitiallyPlaced && this.items.length > 0) {
            this.isInitiallyPlaced = true;
            this.setPinnedEdge(this.defaultEdge);
            this.repinEdge('initial');
        }
    }

    // The key this render will jump to, if any. Landing on the newest item isn't one: that resolves to
    // an edge re-pin, which in reverse writes nothing at all - so a message you just posted still gets
    // to animate in.
    private getPendingJumpKey(rs: VirtualListRenderState): string | null {
        const scrollToKey = rs.scrollToKey;
        if (scrollToKey == null || !this.indexByKey.has(scrollToKey))
            return null;
        if (scrollToKey === this.getLastContentKey() && rs.hasVeryLastItem)
            return null;

        return scrollToKey !== this.handledScrollToKey || !this.visibleKeys.has(scrollToKey)
            ? scrollToKey
            : null;
    }

    // Mirrored onto the root element so the pinned state is visible in the DOM - to the eye while
    // debugging, and to any rule that wants to style a list that is following its edge.
    private setPinnedEdge(edge: VirtualListEdge | null): void {
        const wasPinned = this.pinnedEdge;
        this.pinnedEdge = edge;
        const isStickyEnd = edge === VirtualListEdge.End;
        if (isStickyEnd !== this.ref.classList.contains('sticky-end'))
            this.ref.classList.toggle('sticky-end', isStickyEnd);
        if (wasPinned !== edge)
            this.repinChainAnchor(wasPinned);
    }

    // Letting go of an edge holds what is on screen; taking one hands the placement back to the model.
    private repinChainAnchor(wasPinned: VirtualListEdge | null): void {
        if (!this.isReverse || this.items.length === 0 || !this.stability.isAnimating)
            return;

        const viewTop = this.ref.getBoundingClientRect().top;
        const before = this.containerRef.getBoundingClientRect().top - viewTop;
        this.writeChainPosition();
        if (this.pinnedEdge != null || wasPinned == null)
            return;

        this.chainStart += before - (this.containerRef.getBoundingClientRect().top - viewTop);
        this.writeChainPosition();
    }

    // Re-derived from geometry, and only from a user scroll or a settled list: a render that appends to
    // a pinned list moves the edge away by construction, so re-deriving there would drop the pin exactly
    // when it is needed.
    private updatePinnedEdge(): void {
        const rs = this.renderState;
        if (this.items.length === 0) {
            this.setPinnedEdge(null);
            return;
        }

        // A chain that fits on screen rests with its first item at the top, which leaves the end anchor
        // hanging below the fold - so the end is reached there by construction, however far the anchor
        // says it is. Without this an End-edge list would settle on Start and stop following new
        // messages until the conversation outgrew the viewport.
        const isAtEnd = rs.hasVeryLastItem
            && (this.isChainWithinViewport || (this.distanceToEndEdge() ?? Infinity) <= EdgeEpsilon);
        const isAtStart = rs.hasVeryFirstItem && this.distanceToStartEdge() <= EdgeEpsilon;
        // Both edges reachable at once (content shorter than the viewport) means the preferred one wins.
        this.setPinnedEdge(isAtEnd && (this.defaultEdge === VirtualListEdge.End || !isAtStart)
            ? VirtualListEdge.End
            : isAtStart
                ? VirtualListEdge.Start
                : null);
    }

    private repinEdge(reason: string): void {
        const edge = this.pinnedEdge;
        if (edge == null || this.items.length === 0)
            return;

        // Past a boundary, the position is not the one the user sees - a transform holds the rest of it
        // - so a re-pin measured here would aim at the wrong place, and the write would end the bounce
        // with a snap. The edge it wants is still there once the overscroll is over.
        if (this.scrollController.isOverscrollActive) {
            this.repinWhenOverscrollEnds();
            return;
        }

        let target = this.measureEdgeTarget(edge);
        const delta = target - this.scrollOffset;
        if (Math.abs(delta) < RepinEpsilon)
            return;

        // Anything scrolling could itself have produced is a follow, however large - a message arrived,
        // a transcript grew, the viewport resized under a pinned list. Size alone does not make one a
        // re-placement: on a short viewport a single tall message can exceed a screen, and jumping
        // there would end a fling for an ordinary new message.
        if (Math.abs(delta) <= this.maxOverscroll) {
            this.scheduleFollow(reason);
            return;
        }

        // Further than any scroll could have carried it, so this is a re-placement rather than a follow
        // - opening a chat at its end, coming back from stranded - and a jump is what scrollTop is for.
        // A non-smooth scroll forces a reflow, so re-measuring right after it sees the new geometry;
        // near an edge container.top moves with the scroll, so one pass undershoots.
        for (let pass = 0; pass < RepinMaxPasses; pass++) {
            if (Math.abs(this.scrollOffset - target) < RepinEpsilon)
                return;

            this.setScrollOffset(target);
            target = this.measureEdgeTarget(edge);
        }
        debugLog?.log(`[${this.identity}] repinEdge: ${edge} (${reason})`);
    }

    // Where the pinned edge wants the view, in wrapper coordinates. Measured from the DOM rather than
    // derived from the model: an edge re-pin has to land flush even when the model runs a pixel or two
    // long, and the DOM is the thing the user sees.
    private measureEdgeTarget(edge: VirtualListEdge): number {
        const viewRect = this.ref.getBoundingClientRect();
        const scrollOffset = this.scrollOffset;
        const maxScrollOffset = this.maxScrollTop;
        let delta: number;
        if (edge === VirtualListEdge.Start) {
            const firstRef = this.firstItemRef;
            delta = firstRef
                ? firstRef.getBoundingClientRect().top - viewRect.top
                : -scrollOffset;
        }
        else {
            const anchorRect = this.endAnchorRef.getBoundingClientRect();
            if (anchorRect.height > 0)
                delta = anchorRect.bottom - viewRect.bottom;
            else {
                const lastRef = this.lastItemRef;
                delta = lastRef
                    ? lastRef.getBoundingClientRect().bottom - viewRect.bottom
                    : maxScrollOffset - scrollOffset;
            }
        }
        const target = clamp(scrollOffset + delta, 0, maxScrollOffset);
        // Same cap as computeScrollLimits: the end anchor must not push the first message off the top
        // of a conversation that fits on screen.
        return this.isChainWithinViewport ? Math.min(target, this.chainStart) : target;
    }

    // A follow moves the position the user is at, so it moves the scroll position - the term the
    // compositor and position: sticky read too. Carried in the translation instead, all of those keep
    // pointing at a place the user has left.
    //
    // On a frame, and re-measured there, because a render is not the last thing to move the edge before
    // the frame ends and several renders in one frame must not become several writes. Measured in the
    // read phase and written in the write phase, so it is not this that makes someone else's read
    // synchronous.
    private scheduleFollow(reason: string, isRetry = false): void {
        if (this.isFollowScheduled)
            return;

        this.isFollowScheduled = true;
        let delta = 0;
        fastRaf({
            hz: isRetry ? FollowRetryHz : undefined,
            read: () => delta = this.measureFollow(),
            write: () => {
                this.isFollowScheduled = false;
                if (this.isFollowDeferred) {
                    this.isFollowDeferred = false;
                    this.scheduleFollow(reason, true);
                    return;
                }

                this.applyFollow(delta, reason);
            },
        });
    }

    // Whether the position is this list's to move this frame. A finger owns it while it is down; a
    // scroll of the user's own owns it until it settles, which matters because a fling is in band and
    // does not clear the pin until its first event is read - inside the programmatic-scroll guard, not
    // at once; and an excursion means the position is not the one on screen. Both per-frame corrections
    // ask, and both defer rather than drop: the translation they replaced could land under a finger
    // because a transform cannot fight one, and a correction simply skipped leaves the view off until
    // something else happens to correct it.
    private get canCorrectPosition(): boolean {
        return !this.scrollController.isTouchActive
            && !this.stability.isScrolling
            && !this.scrollController.isOverscrollActive;
    }

    private measureFollow(): number {
        const edge = this.pinnedEdge;
        if (this.isDisposed || edge == null || this.items.length === 0)
            return 0;

        if (!this.canCorrectPosition) {
            // The excursion has its own way back; everything else is retried next frame.
            if (this.scrollController.isOverscrollActive)
                this.repinWhenOverscrollEnds();
            else
                this.isFollowDeferred = true;

            return 0;
        }

        const delta = this.measureEdgeTarget(edge) - this.scrollOffset;
        return Math.abs(delta) < RepinEpsilon ? 0 : delta;
    }

    private applyFollow(delta: number, reason: string): void {
        if (delta === 0 || this.isDisposed)
            return;

        // Renders kept landing while this frame was waited for, and the gap is now further than any
        // scroll could have carried it: that is a re-placement, and repinEdge owns the jump. Dropped
        // instead, the edge stays off until something else happens to re-pin it.
        if (Math.abs(delta) > this.maxOverscroll) {
            this.repinEdge(reason);
            return;
        }

        const applied = this.scrollController.followBy(delta);
        // Nothing landed, so there is no echo to recognise - and a value left over from the last one
        // would swallow a user scroll that happened to stop on it.
        this.lastFollowTop = applied === 0 ? null : this.ref.scrollTop;
        if (applied === 0)
            return;

        debugLog?.log(`[${this.identity}] applyFollow: ${this.pinnedEdge} by ${applied.toFixed(1)} (${reason})`);
        // Nothing else will notice that the window the list is looking at has changed: the scroll event
        // this produces is the list's own, and onScroll drops it.
        this.updateViewportThrottled();
    }

    // Anything that had to wait for the list to stop moving: the edge re-pin an animation grew away
    // from, the clamp that needed the settled sizes, and the direction switch that would have written
    // scrollTop mid-gesture.
    private repinWhenStable(): void {
        if (this.isAwaitingStability)
            return;

        this.isAwaitingStability = true;
        void this.stability.whenStable().then(() => {
            this.isAwaitingStability = false;
            if (this.isDisposed)
                return;

            this.repinEdge('settled');
            // Not when the re-pin booked a follow for the next frame: that write is the correction, and
            // a clamp landing first is exactly the scroll write the re-pin exists to avoid. The follow
            // clamps into the limits itself, and the next settle runs this again.
            if (!this.isFollowScheduled)
                this.scrollController.clampToLimits();
            this.checkModelDrift('settled');
        });
    }

    // The stability tracker watches animations and scroll events, and a return spring produces neither
    // once it has the scroller pinned - so waiting on it can resolve mid-bounce. This waits on the
    // bounce itself.
    private repinWhenOverscrollEnds(): void {
        if (this.isAwaitingOverscrollEnd)
            return;

        this.isAwaitingOverscrollEnd = true;
        const tick = (): void => {
            if (this.isDisposed) {
                this.isAwaitingOverscrollEnd = false;
                return;
            }
            if (this.scrollController.isOverscrollActive) {
                requestAnimationFrame(tick);
                return;
            }

            this.isAwaitingOverscrollEnd = false;
            this.repinEdge('overscroll-ended');
        };
        requestAnimationFrame(tick);
    }

    // A jump's target depends on where the content ends up, so it can't be computed while the DOM is
    // mid-animation. New animations are suppressed while it waits, the ones in flight are awaited, and
    // a later, higher-priority jump supersedes it.
    private requestJump(jump: Jump): void {
        const pending = this.pendingJump;
        if (pending != null && pending.priority > jump.priority)
            return;

        this.pendingJump = jump;
        if (!this.stability.isAnimating) {
            this.runPendingJump();
            return;
        }
        if (this.isAwaitingJump)
            return;

        this.isAwaitingJump = true;
        const whenNoAnimations = this.stability.whenNoAnimations();
        this.heights?.suspendUntil(whenNoAnimations);
        void whenNoAnimations.then(() => {
            this.isAwaitingJump = false;
            this.runPendingJump();
        });
    }

    private runPendingJump(): void {
        const jump = this.pendingJump;
        this.pendingJump = null;
        if (jump == null || this.isDisposed)
            return;

        debugLog?.log(`[${this.identity}] jump: ${jump.reason}`);
        if (jump.target.kind === 'edge') {
            this.setPinnedEdge(jump.target.edge);
            this.repinEdge(jump.reason);
            return;
        }

        const itemRef = this.getItemRef(jump.target.key);
        if (itemRef == null) {
            // Nothing retries the stranded recovery this jump superseded, and the check is otherwise
            // only re-armed by a scroll - which a stranded view gives the user no way to make.
            this.repinIfStrandedDebounced();
            return;
        }

        this.scrollToItem(itemRef, jump.target.position, jump.isSmooth);
    }

    private scrollToItem(itemRef: HTMLElement, position: ScrollLogicalPosition, isSmooth: boolean): void {
        // The author badge, when there is one, is the visual start of a message; without it a grouped
        // message lands with its author header above the viewport.
        const targetRef = itemRef.querySelector('div.c-author-badge') ?? itemRef;
        const viewRect = this.ref.getBoundingClientRect();
        const targetRect = targetRef.getBoundingClientRect();
        const elementTop = this.scrollOffset + (targetRect.top - viewRect.top);
        const target = position === 'center'
            ? elementTop - viewRect.height / 2
            : position === 'end'
                ? elementTop - (viewRect.height - targetRect.height)
                : elementTop;
        // A smooth scroll over more than a screen is a long blur the user can't read anything through.
        this.setScrollOffset(target, isSmooth && Math.abs(target - this.scrollOffset) < viewRect.height);
    }

    // How far the newest content sits below the viewport bottom: 0 when flush, growing as you scroll up.
    // null when there is nothing to measure against.
    private distanceToEndEdge(): number | null {
        const bottom = this.ref.getBoundingClientRect().bottom;
        const anchorRect = this.endAnchorRef.getBoundingClientRect();
        if (anchorRect.height > 0)
            return anchorRect.bottom - bottom;

        const lastRef = this.lastItemRef;
        return lastRef == null ? null : lastRef.getBoundingClientRect().bottom - bottom;
    }

    private distanceToStartEdge(): number {
        const firstRef = this.firstItemRef;
        if (firstRef == null)
            return Infinity;

        return this.ref.getBoundingClientRect().top - firstRef.getBoundingClientRect().top;
    }

    // Both of the things that change the list's coordinates rather than its content - shifting the
    // chain back to the middle of the scroll space, and putting the render direction back after an
    // interaction borrowed it - are invisible at a standstill and a jump anywhere else. So each is a
    // standing intent: it arms when wanted, cancels the moment it stops being wanted, and fires only
    // once the position has held for QuietStillFrames. Nothing here is time-based - a fling delivers no
    // events between frames and would satisfy any event-based settle check while still running.
    private watchQuietMoment(): void {
        if (this.isWatchingStillness)
            return;

        this.isWatchingStillness = true;
        let stillFrames = 0;
        let lastTop = this.ref.scrollTop;
        const tick = (): void => {
            if (this.isDisposed || !this.isChainOffCentre()) {
                this.isWatchingStillness = false;
                return;
            }

            const top = this.ref.scrollTop;
            // An unchanged position is not enough on its own. The spring holds scrollTop at the boundary
            // while it animates and a resting finger holds it just as still, and both carry the
            // displacement in a transform that a re-centre would have to account for. The finger is
            // excluded even without an overscroll: it is about to move again.
            const isMoving = top !== lastTop
                || this.scrollController.isTouchActive
                || this.scrollController.isOverscrollActive;
            stillFrames = isMoving ? 0 : stillFrames + 1;
            lastTop = top;
            // An animation still running means the heights the chain is placed from are not the ones on
            // screen, and both of these re-place the chain.
            if (stillFrames < QuietStillFrames || this.stability.isAnimating) {
                requestAnimationFrame(tick);
                return;
            }

            this.mustRecentre = true;
            this.applyLayout('recentre');
            this.isWatchingStillness = false;
        };
        requestAnimationFrame(tick);
    }

    private computeScrollLimits(): ScrollLimits {
        if (this.items.length === 0)
            return { min: null, max: null };

        const rs = this.renderState;
        // At a discovered edge the chain itself is the limit. Short of one there is no edge to stop at,
        // and scrolling well past what is loaded is how reading further back begins - so this stops only
        // where the window could no longer follow: from further out than MaxOverscrollScreens a query
        // asks around a position the data will never reach, leaving nothing to come back to.
        let min = rs.hasVeryFirstItem
            ? this.chainStart
            : this.chainStart - this.maxOverscroll;
        // The end of the content, which is nowhere near the end of the wrapper. It used to coincide with
        // it in reverse - the wrapper was trimmed to the newest item, so the browser's own scrollTop 0
        // was the edge - and that is exactly what made the two boundaries behave differently: a limit
        // the scroller enforces itself can't rubber-band. Blank space below the newest item is now
        // unreachable only because this says so, the same way the top is.
        let max = rs.hasVeryLastItem
            ? this.chainEnd + this.endAnchorSize - this.ref.clientHeight
            : this.chainEnd + this.maxOverscroll - this.ref.clientHeight;
        // The end anchor is blank space under the newest message, sized to keep it clear of the editor.
        // A whole conversation that fits on screen has nothing to keep clear of anything, and honouring
        // the anchor there scrolls the first message off the top of a list that cannot scroll back.
        if (this.isChainWithinViewport)
            max = Math.min(max, this.chainStart);
        if (min > max) {
            if (this.defaultEdge === VirtualListEdge.End)
                min = max;
            else
                max = min;
        }
        // Whatever the model says, the browser's own band is [0, maxScrollTop] in these coordinates.
        // Short content in reverse is where that matters: the chain starts mid-space while the whole
        // scrollable range collapses to nothing, so an unclamped min sits permanently out of reach and
        // the scroller reads every resting frame as an overscroll to rubber-band back from.
        const limit = this.maxScrollTop;
        return {
            min: this.toScrollTop(clamp(min, 0, limit)),
            max: this.toScrollTop(clamp(max, 0, limit)),
        };
    }

    // Classifies everything this render added the way a text diff would: a key standing where a removed
    // one stood is an edit and grows from that item's height, anything else is an insertion and grows
    // from zero. Extending the loaded range on the far side from the pinned edge is neither - that is a
    // page of older messages arriving, and growing a whole page out of nothing heaves the list.
    // Returns whether anything was parked, i.e. whether the chain got shorter than the model said.
    private runRenderScripts(): void {
        if (InfiniteList.renderScripts.size === 0)
            return;

        // One query per registered name over the rendered window, rather than per item: the window is
        // the only part of the transcript in the DOM, and this runs on every render.
        for (const name of InfiniteList.renderScripts.keys()) {
            const attribute = RenderScriptPrefix + name;
            for (const element of this.containerRef.querySelectorAll<HTMLElement>(`[${attribute}]`))
                this.runRenderScript(element, name, attribute);
        }
    }

    private runRenderScript(element: HTMLElement, name: string, attribute: string): void {
        const value = element.getAttribute(attribute);
        if (value === null)
            return;

        let ran = this.ranRenderScripts.get(element);
        if (ran == null) {
            ran = new Map<string, string>();
            this.ranRenderScripts.set(element, ran);
        }
        if (ran.get(name) === value)
            return;

        ran.set(name, value);
        try {
            InfiniteList.renderScripts.get(name)?.(this, element, value);
        }
        catch (error) {
            warnLog?.log(`[${this.identity}] render script "${name}" failed:`, error);
        }
    }

    private applyAppearances(
        rs: VirtualListRenderState,
        oldKeys: string[],
        oldHeights: Map<string, number>,
        isFullReplacement: boolean,
    ): boolean {
        const heights = this.heights;
        if (heights == null || isFullReplacement || oldKeys.length === 0 || this.items.length === 0)
            return false;
        // A render about to jump somewhere has to measure its target against where the content will be,
        // not against items parked at zero.
        if (this.pendingJump != null || this.getPendingJumpKey(rs) != null)
            return false;
        if (this.revealedAt === 0 || performance.now() - this.revealedAt < AppearanceQuietMs)
            return false;

        const newKeys = this.items.map(x => x.key);
        const isEndEdge = this.defaultEdge === VirtualListEdge.End;
        const edge: ItemKey[] = this.pinnedEdge === this.defaultEdge ? [EdgeSentinel] : [];
        const ops = isEndEdge
            ? diffKeys<ItemKey>([...oldKeys, ...edge], [...newKeys, ...edge])
            : diffKeys<ItemKey>([...edge, ...oldKeys], [...edge, ...newKeys]);
        // The bounds are the outermost ops that came from the old sequence: an addition merging outside
        // them extends the loaded range, one merging inside genuinely arrived. They are not sentinels -
        // a diff has no notion of key order, so a bound token would have nothing to anchor against.
        const minAt = ops.findIndex(x => x.kind !== 'add');
        const maxAt = ops.reduce((acc, x, i) => x.kind !== 'add' ? i : acc, -1);
        // Walked in chain order, i.e. top to bottom: the height controller hands out its animation
        // budget in call order, and the items nearest the top are the ones worth spending it on.
        let hasParked = false;
        for (let i = minAt; i <= maxAt; i++) {
            const op = ops[i];
            if (op.kind !== 'add' || typeof op.key !== 'string')
                continue;

            const index = this.indexByKey.get(op.key);
            if (index == null || !this.isKeyOnScreen(op.key))
                continue;

            // The key was on screen a moment ago, so this is the source having dropped it and put it
            // back rather than anything arriving. The diff cannot see that: the item is absent from the
            // render it is diffed against, which is what an insertion looks like.
            const wasHere = this.recentlyRemoved.get(op.key);
            if (wasHere != null)
                continue;

            const replacedHeight = typeof op.replacedKey === 'string'
                ? oldHeights.get(op.replacedKey)
                : undefined;
            hasParked = heights.beginAppearance(op.key, this.items[index].ref, replacedHeight ?? 0)
                || hasParked;
        }
        return hasParked;
    }

    private onItemHeightChanged(key: string): void {
        const index = this.indexByKey.get(key);
        if (index == null)
            return;

        const height = this.heights?.getHeight(key);
        if (height == null || height === this.items[index].height)
            return;

        this.items[index].height = height;
        // Mid-render the offsets belong to the previous item set, so a re-layout here would anchor
        // against a chain that no longer exists; applyRender lays out once at the end anyway.
        if (!this.isApplyingRender)
            this.relayoutThrottled();
    }

    private onResize = (entries: ResizeObserverEntry[]): void => {
        let hasItemChanges = false;
        let isViewportResized = false;
        for (const entry of entries) {
            const target = entry.target as HTMLElement;
            if (target === this.ref) {
                isViewportResized = true;
                continue;
            }
            if (target === this.endAnchorRef) {
                const size = getBlockSize(entry);
                if (Math.abs(size - this.endAnchorSize) < 0.5)
                    continue;

                this.endAnchorSize = size;
                hasItemChanges = true;
                continue;
            }

            const key = target.dataset.key;
            const index = key == null ? undefined : this.indexByKey.get(key);
            if (key == null || index == null)
                continue;

            const height = getBlockSize(entry);
            if (Math.abs(height - this.items[index].height) < 0.5)
                continue;
            if (height === 0 && !isLaidOut(target))
                continue;

            this.items[index].height = height;
            hasItemChanges = true;
        }

        if (isViewportResized) {
            this.updateWindowScrollTopForIos();
            // The viewport changed under the user - a keyboard, a panel - so whatever edge they were
            // parked at has to follow it. Applied now and again once the list settles: the target comes
            // from the DOM, and mid-animation the DOM is not where the content will end up.
            this.repinEdge('viewport-resize');
            this.repinWhenStable();
        }
        if (hasItemChanges)
            this.relayoutThrottled();
    };

    private readonly relayoutThrottled = throttle(
        () => this.relayout(),
        UpdateViewportIntervalMs,
        'default');

    private relayout(): void {
        if (this.isDisposed || this.items.length === 0)
            return;

        const oldOffsets = this.offsets;
        const oldChainStart = this.chainStart;
        const oldKeys = this.items.map(x => x.key);
        this.computeOffsets();
        this.reanchor(oldKeys, oldOffsets, oldChainStart);
        this.applyLayout('relayout');
        if (this.pinnedEdge != null) {
            this.repinEdge('relayout');
            this.repinWhenStable();
        }
        this.updateViewportThrottled();
    }

    // Whether the scroll is standing exactly where the last follow put it, which makes the event it is
    // about this list's own write. A guard window would be the obvious way to do this and the wrong
    // one: a growing transcript writes one follow per render, so the window would never close, and it
    // would swallow the swipe that is supposed to end the pin - which is exactly how the view used to
    // get trapped at the bottom. One value instead, so the user's very first event past it is theirs.
    private isFollowEcho(): boolean {
        return this.lastFollowTop != null
            && Math.abs(this.ref.scrollTop - this.lastFollowTop) <= RepinEpsilon;
    }

    private onScroll = (event: Event): void => {
        if (this.isFollowEcho())
            return;

        this.lastFollowTop = null;
        this.stability.holdScroll(ScrollSettleMs);
        this.turnOffIsScrollingDebounced();
        if (!event.isTrusted)
            return;
        // The scroll a re-pin or a re-centre just wrote isn't the user moving, and reading it as one
        // would drop the very pin that produced it.
        if (performance.now() - this.lastProgrammaticScrollAt < ProgrammaticScrollGuardMs)
            return;

        this.interactiveAnchor = null;
        this.releaseScreenAnchor();
        this.updatePinnedEdge();
        this.updateViewportThrottled();
    };

    private onScrollEnd = (): void => {
        // A follow ends with one of these too, and it is not a gesture ending: turnOffIsScrolling
        // re-derives the pinned edge, and doing that from the list's own write means re-deriving it
        // mid-animation, when the rendered edge is the one thing that is not settled.
        if (this.isFollowEcho())
            return;

        this.turnOffIsScrolling();
    };

    private onDocumentVisibilityChange = (): void => {
        this.updateVisibilityThrottled();
    };

    // A wheel gesture says the user wants to leave the edge even when the scroll it produces lands
    // inside the programmatic-scroll guard - which it routinely does while content is resizing and
    // re-pinning. Without this the pin survives and the next re-pin drags the view back.
    private onWheel = (event: WheelEvent): void => {
        // Momentum scrolling on mobile also arrives as wheel events; there onScroll is enough. A
        // ctrl+wheel is a pinch-zoom, which doesn't scroll the list at all.
        if (DeviceInfo.isMobile || event.ctrlKey || this.pinnedEdge == null)
            return;

        const isAwayFromEdge = this.pinnedEdge === VirtualListEdge.End
            ? event.deltaY < 0
            : event.deltaY > 0;
        if (isAwayFromEdge)
            this.setPinnedEdge(null);
    };

    private readonly turnOffIsScrollingDebounced = debounce(() => this.turnOffIsScrolling(), ScrollSettleMs);

    private turnOffIsScrolling(): void {
        if (this.isDisposed)
            return;

        this.stability.releaseScroll();
        this.updatePinnedEdge();
        this.updateVisibilityThrottled();
        this.updateViewportThrottled();
        this.repinIfStrandedDebounced();
    }

    // Only controls that opt in via data-vl-hold arm an interactive anchor; plain taps - play, links,
    // text selection - must not affect anchoring. "always" holds the item and leaves the edge, a
    // deliberate "read history" action; "keep-edge" holds only when not pinned, since a pinned list
    // absorbs the size change through its edge re-pin instead.
    private onInteractiveEvent = (event: Event): void => {
        const target = event.target as HTMLElement | null;
        // The listener is on the document, so every list in the app hears every click and touch.
        if (target == null || !this.containerRef.contains(target))
            return;

        const holdRef = target.closest<HTMLElement>('[data-vl-hold]');
        if (holdRef == null)
            return;

        if (holdRef.dataset.vlHold === 'keep-edge' && this.pinnedEdge != null)
            return;

        // data-vl-anchor beats a key, and is the only thing that works when the control is inside
        // something the list doesn't track as an item, or when what it sits in is replaced wholesale
        // by the render it triggers - expanding a collapsed block is both.
        const anchorRef = holdRef.closest<HTMLElement>('[data-vl-anchor]');
        const anchorId = anchorRef?.dataset.vlAnchor;
        if (anchorRef != null && anchorId) {
            this.setPinnedEdge(null);
            this.interactiveAnchor = null;
            this.screenAnchor = {
                id: anchorId,
                top: anchorRef.getBoundingClientRect().top - this.ref.getBoundingClientRect().top,
                at: performance.now(),
                isPlaced: false,
                corrected: 0,
            };
            return;
        }

        const key = holdRef.closest<HTMLElement>('.item[data-key]')?.dataset.key;
        if (key == null)
            return;

        this.setPinnedEdge(null);
        // A control marked data-anchor="below" reveals rows ABOVE itself, so the item below it is what
        // must keep its screen position - otherwise the revealed rows push everything downward.
        const anchorKey = target.closest('[data-anchor="below"]') != null
            ? this.getFirstContentKeyBelow(key) ?? key
            : key;
        this.interactiveAnchor = { key: anchorKey, at: performance.now() };
    };

    // Puts the anchored element back where it was on screen. Both readings are rendered positions,
    // so an element that was stuck to the viewport edge is put back where the user saw it rather than
    // where the flow would have had it - which is the case this exists for.
    private applyScreenAnchor(): boolean {
        const anchor = this.screenAnchor;
        if (anchor == null)
            return false;
        const ttl = anchor.isPlaced ? InteractiveAnchorTtlMs : ScreenAnchorRenderTtlMs;
        if (performance.now() - anchor.at > ttl) {
            this.releaseScreenAnchor();
            return false;
        }

        const anchorRef = this.containerRef
            .querySelector<HTMLElement>(`:scope [data-vl-anchor="${CSS.escape(anchor.id)}"]`);
        if (anchorRef == null)
            return false;

        // Placed once, on the render the interaction caused. Re-placing it on every render that
        // follows drags the view further off each time, because those renders are the ones growing the
        // revealed items in from zero and the target was measured before any of them existed. What
        // holds it through that growth is watchScreenAnchor, which corrects per frame instead.
        if (anchor.isPlaced) {
            this.watchScreenAnchor();
            return false;
        }

        anchor.isPlaced = true;
        anchor.at = performance.now();
        this.watchScreenAnchor();
        // Against where the chain is now, not where it was written: this runs before applyLayout, and
        // twice per render when appearing items are parked.
        this.writeChainPosition();
        // Where the element would sit if neither it nor its owning list item were stuck to the viewport
        // edge. A stuck element reports the edge it is stuck to, and no amount of moving the chain
        // changes that - so aiming at its rendered position would move the chain without moving the
        // element, over and over. Its flow position is the thing that follows.
        const stickyRef = anchorRef.closest<HTMLElement>(`.${StickyItemClass}`);
        const position = anchorRef.style.position;
        const stickyPosition = stickyRef?.style.position ?? '';
        anchorRef.style.position = 'static';
        if (stickyRef != null)
            stickyRef.style.position = 'static';
        const flowTop = anchorRef.getBoundingClientRect().top - this.ref.getBoundingClientRect().top;
        anchorRef.style.position = position;
        if (stickyRef != null)
            stickyRef.style.position = stickyPosition;
        this.chainStart += anchor.top - flowTop;
        return true;
    }

    // A height animation makes the DOM disagree with the model for as long as it runs, and in reverse
    // that difference moves everything above the growth: the container is anchored by its bottom, so
    // its rendered top is that edge minus the rendered height, and the rendered height is short until
    // the animation lands. A re-layout puts it back, but it runs at 64ms and the animation changes
    // heights every frame - which is the "it almost scrolls, then returns" the anchor alone cannot fix.
    // So the anchored element is held per frame, by the translation, until the list settles.
    private watchScreenAnchor(): void {
        if (this.isWatchingScreenAnchor)
            return;

        this.isWatchingScreenAnchor = true;
        let stillFrames = 0;
        let watched = this.screenAnchor;
        const tick = (): void => {
            const anchor = this.screenAnchor;
            // A second interaction can replace the anchor while this loop still runs, and the frames it
            // has counted belong to the one that is gone: adopting them retires the new anchor early,
            // dropping the hold before the render it was taken for has arrived.
            if (anchor !== watched) {
                watched = anchor;
                stillFrames = 0;
            }
            // The same TTL rule the other two readers use: an anchor that has not been placed yet is
            // waiting on a render, which on WASM can be seconds away.
            const ttl = anchor?.isPlaced === false ? ScreenAnchorRenderTtlMs : InteractiveAnchorTtlMs;
            if (this.isDisposed || anchor == null || performance.now() - anchor.at > ttl) {
                this.releaseScreenAnchor();
                return;
            }

            // A frame the list may not write is not the element standing still: counting it would
            // release the hold with the correction still owing - twelve frames of a resting finger is
            // all it takes.
            const isDeferred = !this.canCorrectPosition;
            const moved = isDeferred ? false : this.correctScreenAnchor();
            if (moved == null) {
                this.releaseScreenAnchor();
                return;
            }

            stillFrames = moved || isDeferred ? 0 : stillFrames + 1;
            // Held until the element itself stops moving, not until the stability tracker calls the
            // animation over: the model reaches the settled heights first, and re-laying out to them
            // while the DOM is still transitioning towards them moves the whole chain by what is left
            // to grow - measured at 349px, arriving one frame after the tracker went quiet.
            if (stillFrames >= ScreenAnchorStillFrames && !this.stability.isAnimating) {
                this.releaseScreenAnchor();
                return;
            }

            requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
    }

    private releaseScreenAnchor(): void {
        this.isWatchingScreenAnchor = false;
        this.screenAnchor = null;
    }


    // Puts the anchored element back where the interaction left it. Returns whether it had to move, or
    // null when there is nothing left to hold - the anchor expired, or the correction has run away.
    private correctScreenAnchor(): boolean | null {
        const anchor = this.screenAnchor;
        if (anchor == null || this.isDisposed || performance.now() - anchor.at > InteractiveAnchorTtlMs)
            return null;

        const anchorRef = this.containerRef
            .querySelector<HTMLElement>(`:scope [data-vl-anchor="${CSS.escape(anchor.id)}"]`);
        if (anchorRef == null)
            return false;

        // Rendered, not flow: this holds what is on screen against content moving under it. An element
        // the viewport edge has stuck is already still and reads a delta of zero, which is right for it.
        const top = anchorRef.getBoundingClientRect().top - this.ref.getBoundingClientRect().top;
        const delta = anchor.top - top;
        if (Math.abs(delta) < RepinEpsilon)
            return false;

        // Negated: `delta` is where the element has to go, and moving the view back moves the content
        // forward. A scroll write rather than a translation for the follow's reason (§3.6) and one of
        // its own: the only gesture that starts a hold is a tap on a control inside a conversation
        // header, so there is no scrolling of the user's here for a scroll write to fight.
        const applied = this.scrollController.followBy(-delta);
        if (applied === 0)
            return false;

        this.lastFollowTop = this.ref.scrollTop;
        // A runaway guard, not a size limit: expanding a block moves the chain by the whole of what is
        // still growing - measured at 349px - and holding an anchor through that is the entire point.
        // What it catches is the loop feeding itself, which doubles every frame and would reach any
        // bound at all within about thirty of them.
        anchor.corrected += applied;
        return Math.abs(anchor.corrected) > this.maxOverscroll ? null : true;
    }

    private hasFreshScreenAnchor(): boolean {
        const anchor = this.screenAnchor;
        if (anchor == null)
            return false;
        const ttl = anchor.isPlaced ? InteractiveAnchorTtlMs : ScreenAnchorRenderTtlMs;
        if (performance.now() - anchor.at > ttl) {
            this.releaseScreenAnchor();
            return false;
        }

        return true;
    }

    private getFreshInteractiveAnchorKey(): string | null {
        const anchor = this.interactiveAnchor;
        if (anchor == null)
            return null;
        if (performance.now() - anchor.at > InteractiveAnchorTtlMs) {
            this.interactiveAnchor = null;
            return null;
        }

        return anchor.key;
    }

    private getFirstContentKeyBelow(key: string): string | null {
        const index = this.indexByKey.get(key);
        if (index == null)
            return null;

        for (let i = index + 1; i < this.items.length; i++)
            if (!this.items[i].mustSkipKey)
                return this.items[i].key;

        return null;
    }

    private onItemVisibilityChanged = (entries: IntersectionObserverEntry[]): void => {
        let hasChanged = false;
        for (const entry of entries) {
            const key = (entry.target as HTMLElement).dataset.key;
            if (key == null)
                continue;

            const index = this.indexByKey.get(key);
            // The last clause covers an item taller than the viewport: it can never reach 40%, but if
            // all of it that fits is showing, it is what the user is looking at.
            const isVisible = index != null
                && !this.items[index].mustSkipKey
                && entry.isIntersecting
                && (entry.intersectionRatio >= 0.4
                    || entry.intersectionRect.height > MinViewportSize / 2
                    || entry.boundingClientRect.height <= entry.intersectionRect.height + VisibilityEpsilon);
            if (isVisible === this.visibleKeys.has(key))
                continue;

            hasChanged = true;
            if (isVisible)
                this.visibleKeys.add(key);
            else
                this.visibleKeys.delete(key);
        }
        if (hasChanged)
            this.updateVisibilityThrottled();
    };

    private readonly updateVisibilityThrottled = throttle(
        () => this.updateVisibility(),
        UpdateVisibilityIntervalMs,
        'delayHead');

    private updateVisibility(): void {
        if (this.isDisposed)
            return;

        // An empty list has to say so once, or a switch to a result with nothing in it leaves the
        // consumer holding the keys from before. Only once both ends are known, though: "no items
        // yet" during the first load is a different statement, and reporting it would act on it.
        if (this.items.length === 0
            && !(this.renderState.hasVeryFirstItem && this.renderState.hasVeryLastItem))
            return;

        const visibleKeys = [...this.visibleKeys];
        // "Nothing is visible" is only true of a settled list: a fast fling routinely outruns rendering
        // and empties the viewport for a few frames, and reporting that makes the app act on a position
        // the user is not at. turnOffIsScrolling re-reports once the list settles, so nothing is lost.
        if (visibleKeys.length === 0 && this.stability.isScrolling)
            return;

        // Never true while the tab is in the background: the list stays flush at its edge there, and the
        // chat view reads this flag as "the user is looking at the newest message" to advance the read
        // position - so reporting it would mark messages read that nobody has seen.
        // A conversation that fits on screen rests with its first item at the top, which leaves the end
        // anchor's blank space hanging below the fold - the newest message is on screen all the same,
        // and without this it would never be marked read.
        const isEndAnchorVisible = !document.hidden
            && this.renderState.hasVeryLastItem
            && (this.isChainWithinViewport || (this.distanceToEndEdge() ?? Infinity) <= EdgeEpsilon);
        void this.reportVisibility(visibleKeys.sort(), isEndAnchorVisible);
    }

    // Tracked per spacer rather than recomputed from the callback's entries: a callback carrying only
    // the spacer that just left would otherwise report "no skeleton" while the other one is still on
    // screen, and with these thresholds nothing may correct that for a long time.
    private onSkeletonVisibilityChanged = (entries: IntersectionObserverEntry[]): void => {
        for (const entry of entries) {
            const isShown = entry.isIntersecting && entry.boundingClientRect.height > EdgeEpsilon;
            if (entry.target === this.spacerRef)
                this.isStartSkeletonShown = isShown;
            else if (entry.target === this.endSpacerRef)
                this.isEndSkeletonShown = isShown;
        }
        const isNearSkeleton = this.isStartSkeletonShown || this.isEndSkeletonShown;
        if (this.isNearSkeleton === isNearSkeleton)
            return;

        this.isNearSkeleton = isNearSkeleton;
        if (isNearSkeleton)
            this.updateViewportThrottled();
    };

    private readonly updateViewportThrottled = throttle(
        () => void this.requestData(),
        UpdateViewportIntervalMs,
        'default');

    private isKeyOnScreen(key: string): boolean {
        const index = this.indexByKey.get(key);
        if (index == null)
            return false;

        const clientHeight = this.ref.clientHeight;
        if (clientHeight <= 0)
            return false;

        const scrollOffset = this.scrollOffset;
        const top = this.chainStart + this.offsets[index];
        return top + this.items[index].height > scrollOffset && top < scrollOffset + clientHeight;
    }

    private getRetainedRange(viewport: NumberRange): NumberRange | null {
        const measured = this.items.filter(x => x.height >= 0);
        if (measured.length === 0)
            return null;

        const visible = measured.filter(x => {
            const top = this.topOf(x);
            return top + x.height > viewport.start && top < viewport.end;
        });
        const kept = visible.length > 0
            ? visible
            : [...measured]
                .sort((a, b) => this.distanceToViewportCentre(a, viewport) - this.distanceToViewportCentre(b, viewport))
                .slice(0, this.retainedItemCount)
                .sort((a, b) => this.topOf(a) - this.topOf(b));
        const last = kept[kept.length - 1];
        return new NumberRange(this.topOf(kept[0]), this.topOf(last) + last.height);
    }

    private distanceToViewportCentre(item: InfiniteListItem, viewport: NumberRange): number {
        return Math.abs(this.topOf(item) + item.height / 2 - (viewport.start + viewport.end) / 2);
    }

    private firstItemEndingAfter(items: InfiniteListItem[], offset: number): InfiniteListItem {
        for (const item of items)
            if (this.topOf(item) + item.height >= offset)
                return item;

        return items[items.length - 1];
    }

    private lastItemStartingBefore(items: InfiniteListItem[], offset: number): InfiniteListItem {
        for (let i = items.length - 1; i >= 0; i--)
            if (this.topOf(items[i]) <= offset)
                return items[i];

        return items[0];
    }

    private getLastContentKey(): string | null {
        for (let i = this.items.length - 1; i >= 0; i--)
            if (!this.items[i].mustSkipKey)
                return this.items[i].key;

        return null;
    }

    private readonly repinIfStrandedDebounced = debounce(() => this.repinIfStranded(), ScrollSettleMs);

    // Safety net: if the viewport ends up catastrophically far from the chain, nothing on screen can
    // re-pin it, so snap back to the preferred edge.
    private repinIfStranded(): void {
        if (this.isDisposed || this.items.length === 0 || this.stability.isScrolling)
            return;

        const clientHeight = this.ref.clientHeight;
        if (clientHeight <= 0)
            return;

        const scrollOffset = this.scrollOffset;
        const gap = scrollOffset + clientHeight < this.chainStart
            ? this.chainStart - (scrollOffset + clientHeight)
            : scrollOffset > this.chainEnd
                ? scrollOffset - this.chainEnd
                : 0;
        // Overscrolling is normal and computeScrollLimits already bounds it; past a multiple of that
        // same allowance the view and its chain have come apart, whatever the cause.
        if (gap < this.maxOverscroll * StrandedGapFactor)
            return;

        warnLog?.log(`[${this.identity}] repinIfStranded: gap=${gap}, re-pinning to ${this.defaultEdge}`);
        this.requestJump({
            target: { kind: 'edge', edge: this.defaultEdge },
            isSmooth: false,
            priority: JumpPriority.stranded,
            reason: 'stranded',
        });
    }

    // The wrapper stays CSS-hidden until the chain is positioned, so the user never sees the frames
    // before the initial scroll lands.
    private startRevealWatch(): void {
        const startedAt = performance.now();
        const check = (): void => {
            if (this.isDisposed || this.isContainerRevealed)
                return;

            if (this.isContentPlaced() || performance.now() - startedAt > RevealTimeoutMs)
                this.reveal();
            else
                requestAnimationFrame(check);
        };
        requestAnimationFrame(check);
    }

    private isContentPlaced(): boolean {
        const rs = this.renderState;
        if (this.items.length === 0)
            // A confirmed-empty list has nothing to position; a still-loading one has neither end known.
            return rs.hasVeryFirstItem && rs.hasVeryLastItem;

        const viewRect = this.ref.getBoundingClientRect();
        if (viewRect.height <= 0)
            return false;

        const scrollToKey = rs.scrollToKey;
        if (scrollToKey != null) {
            const itemRef = this.getItemRef(scrollToKey);
            if (itemRef == null)
                return false;

            const rect = itemRef.getBoundingClientRect();
            return rect.height > 0 && rect.bottom > viewRect.top && rect.top < viewRect.bottom;
        }

        // Fitting on screen means reaching neither edge, and the slack below the top is where it rests -
        // so this asks only that the first item isn't clipped off the top.
        if (this.isChainWithinViewport)
            return this.distanceToStartEdge() <= RevealEpsilon;

        return this.defaultEdge === VirtualListEdge.End
            ? Math.abs(this.distanceToEndEdge() ?? Infinity) <= RevealEpsilon
            : Math.abs(this.distanceToStartEdge()) <= RevealEpsilon;
    }

    private updateWindowScrollTopForIos(): void {
        if (!DeviceInfo.isIos)
            return;

        // Keeps the text editor visible when the virtual keyboard appears or a message is submitted.
        const isPageScrolled = (window.visualViewport?.offsetTop ?? window.scrollY) !== 0;
        InfiniteList.setPageLock(this, isPageScrolled);
    }

    // The lock is on the document, so it belongs to whoever still needs it rather than to whoever
    // wrote it last: one list disposing while another still has the keyboard up would otherwise hand
    // the page back underneath it. Released on dispose as well - a list torn down with the keyboard
    // open left html and body fixed for whatever came next, with nothing remaining to undo it.
    private static setPageLock(owner: InfiniteList, mustLock: boolean): void {
        const owners = InfiniteList.pageLockOwners;
        if (mustLock)
            owners.add(owner);
        else
            owners.delete(owner);

        const html = document.documentElement;
        const body = document.body;
        if (owners.size !== 0) {
            // Taken from the first lock rather than assumed: releasing to 'static' would overwrite
            // whatever the page had inline before, which was never this component's to decide.
            InfiniteList.pageLockSnapshot ??= {
                htmlPosition: html.style.position,
                htmlOverflowX: html.style.overflowX,
                bodyPosition: body.style.position,
                bodyOverflowX: body.style.overflowX,
            };
            html.style.position = 'fixed';
            html.style.overflowX = 'hidden';
            body.style.position = 'fixed';
            body.style.overflowX = 'hidden';
            return;
        }

        const snapshot = InfiniteList.pageLockSnapshot;
        if (snapshot == null)
            return;

        InfiniteList.pageLockSnapshot = null;
        html.style.position = snapshot.htmlPosition;
        html.style.overflowX = snapshot.htmlOverflowX;
        body.style.position = snapshot.bodyPosition;
        body.style.overflowX = snapshot.bodyOverflowX;
    }

    // Every scroll target the list computes comes out of the model, so a model that drifts from the DOM
    // is the one failure that breaks everything downstream silently. Off by default; enabled through
    // debugUI.virtualListDebug(true).
    private checkModelDrift(reason: string): void {
        if (!InfiniteList.isDebugEnabled || this.items.length < 2 || this.stability.isAnimating)
            return;

        // Sticky items - the conversation headers - report where they are stuck rather than where they
        // sit in the flow, so both the baseline and every comparison have to come from an item that
        // isn't one, or the whole chain below a stuck header reads as drifted.
        const isSticky = (item: InfiniteListItem): boolean =>
            getComputedStyle(item.ref).position === 'sticky';
        const base = this.items.findIndex(x => !isSticky(x));
        if (base < 0)
            return;

        const baseTop = this.items[base].ref.getBoundingClientRect().top;
        let worstKey = '';
        let worstDrift = 0;
        for (let i = base + 1; i < this.items.length; i++) {
            const item = this.items[i];
            if (isSticky(item))
                continue;

            const modelDelta = this.offsets[i] - this.offsets[base];
            const drift = item.ref.getBoundingClientRect().top - baseTop - modelDelta;
            if (Math.abs(drift) > Math.abs(worstDrift)) {
                worstDrift = drift;
                worstKey = item.key;
            }
        }
        this.checkContentOverflow(reason);
        if (Math.abs(worstDrift) <= DriftWarnThresholdPx)
            return;

        warnLog?.log(
            `[${this.identity}] model drift after ${reason}: #${worstKey} is ${worstDrift.toFixed(1)}px `
            + `off the model (${this.items.length} items)`);
    }

    // A settled item has to be exactly as tall as what it contains: it isn't clipped then, so anything
    // that doesn't fit is painted straight over the item below. The usual cause is something the height
    // isn't reserving - a margin on the content element, or a second element the item shouldn't have.
    private checkContentOverflow(reason: string): void {
        if (this.heights == null)
            return;

        for (const item of this.items) {
            if (item.ref.classList.contains('c-height-unsettled'))
                continue;

            const required = getRequiredHeight(item.ref);
            if (required == null) {
                warnLog?.log(
                    `[${this.identity}] content overflow after ${reason}: #${item.key} renders `
                    + `${item.ref.children.length} elements, only the first one is measured`);
                continue;
            }

            const box = item.ref.getBoundingClientRect().height;
            const overflow = required - box;
            if (overflow > ContentOverflowThresholdPx)
                warnLog?.log(
                    `[${this.identity}] content overflow after ${reason}: #${item.key} needs `
                    + `${required.toFixed(1)}px but its box is ${box.toFixed(1)}px - `
                    + `${overflow.toFixed(1)}px paints over the item below`);
        }
    }
}

// What an item's box has to be for its content to fit inside it: the content element's own box plus
// everything around it that the item is responsible for reserving.
function getRequiredHeight(itemRef: HTMLElement): number | null {
    const contentRef = itemRef.firstElementChild;
    if (contentRef == null || itemRef.children.length !== 1)
        return null;

    const itemStyle = getComputedStyle(itemRef);
    const contentStyle = getComputedStyle(contentRef);
    return contentRef.getBoundingClientRect().height
        + (parseFloat(contentStyle.marginTop) || 0)
        + (parseFloat(contentStyle.marginBottom) || 0)
        + (parseFloat(itemStyle.paddingTop) || 0)
        + (parseFloat(itemStyle.paddingBottom) || 0)
        + (parseFloat(itemStyle.borderTopWidth) || 0)
        + (parseFloat(itemStyle.borderBottomWidth) || 0);
}

// The only owner of the spacers' geometry and visibility: size zero is what "hidden" means, so
// there is nothing else to keep in step with it. Written only when it changes, so a render that moves
// nothing touches no styles.
function setSpacerSize(spacerRef: HTMLElement, size: number): void {
    const display = size > 0 ? 'flex' : 'none';
    if (spacerRef.style.display !== display)
        spacerRef.style.display = display;
    const height = `${size}px`;
    if (spacerRef.style.height !== height)
        spacerRef.style.height = height;
}

// An inset the element actually uses, in px, or null for the `auto` every engine reports for one its
// stylesheet never set. An unset inset is a clamp the element does not have, and giving it one here
// would stick it against an edge it was never meant to reach.
function readInset(value: string): number | null {
    const result = Number.parseFloat(value);
    return Number.isFinite(result) ? result : null;
}

function isLaidOut(itemRef: HTMLElement): boolean {
    return itemRef.getClientRects().length > 0;
}

// contentRect is always content-box even under box:'border-box', which undersizes anything whose
// spacing uses padding - so borderBoxSize wins wherever the browser reports it.
function getBlockSize(entry: ResizeObserverEntry): number {
    return entry.borderBoxSize.length > 0 ? entry.borderBoxSize[0].blockSize : entry.contentRect.height;
}

function sameKeys(oldKeys: string[], items: InfiniteListItem[]): boolean {
    if (oldKeys.length !== items.length)
        return false;

    for (let i = 0; i < oldKeys.length; i++)
        if (oldKeys[i] !== items[i].key)
            return false;

    return true;
}

function isSameQuery(x: VirtualListDataQuery, y: VirtualListDataQuery): boolean {
    return x.keyRange.start === y.keyRange.start
        && x.keyRange.end === y.keyRange.end
        && x.moveRange.start === y.moveRange.start
        && x.moveRange.end === y.moveRange.end;
}

// Largest i in [0, count) with offsets[i] <= value, or 0 when value is below the whole range.
function lowerBound(offsets: number[], count: number, value: number): number {
    let low = 0;
    let high = count - 1;
    while (low < high) {
        const mid = (low + high + 1) >> 1;
        if (offsets[mid] <= value)
            low = mid;
        else
            high = mid - 1;
    }
    return low;
}

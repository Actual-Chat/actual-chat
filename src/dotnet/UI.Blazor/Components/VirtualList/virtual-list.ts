import './virtual-list.css';
import { delayAsync, throttle, serialize, PromiseSource, debounce } from 'promises';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListStickyEdgeState } from './ts/virtual-list-sticky-edge-state';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { NumberRange, Range } from './ts/range';
import { VirtualListStatistics } from './ts/virtual-list-statistics';
import { clamp } from './ts/math';
import { Pivot } from './ts/pivot';

import { Log, LogLevel } from 'logging';
import { VirtualListItem } from './ts/virtual-list-item';

const LogScope = 'VirtualList';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const UpdateViewportInterval: number = 64;
const UpdateVisibleKeysInterval: number = 250;
const IronPantsHandlePeriod: number = 1600;
const PivotSyncEpsilon: number = 16;
const VisibilityEpsilon: number = 4;
const EdgeEpsilon: number = 4;
const MaxExpandBy: number = 320;
const RenderTimeout: number = 640;
const UpdateTimeout: number = 1200;
const DefaultLoadZone: number = 2000;
const ScrollDebounce: number = 600;
const SkeletonDetectionBoundary: number = 300;

export class VirtualList {
    /** ref to div.virtual-list */
    private readonly _ref: HTMLElement;
    private readonly _containerRef: HTMLElement;
    private readonly _renderStateRef: HTMLElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _spacerRef: HTMLElement;
    private readonly _endSpacerRef: HTMLElement;
    private readonly _renderIndexRef: HTMLElement;
    private readonly _abortController: AbortController;
    private readonly _renderEndObserver: MutationObserver;
    private readonly _sizeObserver: ResizeObserver;
    private readonly _visibilityObserver: IntersectionObserver;
    private readonly _scrollPivotObserver: IntersectionObserver;
    private readonly _skeletonObserver0: IntersectionObserver;
    private readonly _skeletonObserver1: IntersectionObserver;
    private readonly _skeletonObserver2: IntersectionObserver;
    private readonly _ironPantsIntervalHandle: number;
    private readonly _unmeasuredItems: Set<string>;
    private readonly _visibleItems: Set<string>;
    private readonly _items: Map<string, VirtualListItem>;
    private readonly _statistics: VirtualListStatistics = new VirtualListStatistics();

    private _isDisposed = false;
    private _stickyEdge: Required<VirtualListStickyEdgeState> | null = null;
    private _whenRenderCompleted: PromiseSource<void> | null = null;
    private _whenUpdateCompleted: PromiseSource<void> | null = null;
    private _pivots: Pivot[] = [];
    private _top: number;

    private _isRendering: boolean = false;
    private _isNearSkeleton: boolean = false;
    private _isScrolling: boolean = false;
    private _scrollTime: number | null = null;
    private _scrollDirection: 'up' | 'down' | 'none' = 'none';

    private _query: VirtualListDataQuery = VirtualListDataQuery.None;
    private _lastQuery: VirtualListDataQuery = VirtualListDataQuery.None;
    private _lastQueryTime: number | null = null;

    private _renderState: VirtualListRenderState;
    private _orderedItems: VirtualListItem[] = [];
    private _itemRange: NumberRange | null = null;
    private _viewport: NumberRange | null = null;
    private _shouldRecalculateItemRange: boolean = true;

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
    ) {
        return new VirtualList(ref, backendRef);
    }

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
    ) {
        if (debugLog) {
            debugLog.log(`constructor`);
            window['virtualList'] = this;
        }

        this._ref = ref;
        this._blazorRef = backendRef;
        this._isDisposed = false;
        this._abortController = new AbortController();
        this._spacerRef = this._ref.querySelector(':scope > .spacer-start');
        this._endSpacerRef = this._ref.querySelector(':scope > .spacer-end');
        this._containerRef = this._ref.querySelector(':scope > .virtual-container');
        this._renderStateRef = this._ref.querySelector(':scope > .data.render-state');
        this._renderIndexRef = this._ref.querySelector(':scope > .data.render-index');

        // Events & observers
        const listenerOptions = { signal: this._abortController.signal };
        this._ref.addEventListener('scroll', this.onScroll, listenerOptions);
        this._renderEndObserver = new MutationObserver(this.maybeOnRenderEnd);
        this._renderEndObserver.observe(
            this._renderIndexRef,
            { attributes: true, attributeFilter: ['data-render-index'] });
        this._renderEndObserver.observe(this._containerRef, { childList: true });
        this._sizeObserver = new ResizeObserver(this.onResize);
        // An array of numbers between 0.0 and 1.0, specifying a ratio of intersection area to total bounding box area for the observed target.
        // Trigger callbacks as early as it can on any intersection change, even 1 percent
        const visibilityThresholds = [...Array(101).keys() ].map(i => i / 100);
        this._visibilityObserver = new IntersectionObserver(
            this.onItemVisibilityChange,
            {
                // Track visibility as intersection of virtual list viewport, not the window!
                root: this._ref,
                // Extend visibility outside of the viewport.
                rootMargin: `${VisibilityEpsilon}px`,
                threshold: visibilityThresholds,

                /* required options for IntersectionObserver v2*/
                // @ts-ignore
                trackVisibility: true,
                delay: 250,  // minimum 100
            });
        this._scrollPivotObserver = new IntersectionObserver(
            this.onScrollPivotVisibilityChange,
            {
                root: this._ref,
                // track fully visible items
                rootMargin: `${VisibilityEpsilon}px`,
                // Receive callback on any intersection change, even 1 percent.
                threshold: visibilityThresholds,
            });
        this._skeletonObserver0 = new IntersectionObserver(
            this.onSkeletonVisibilityChange,
            {
                root: this._ref,
                // Extend visibility outside of the viewport
                rootMargin: `-5px`,
                threshold: visibilityThresholds,
            });
        this._skeletonObserver1 = new IntersectionObserver(
            this.onSkeletonVisibilityChange,
            {
                root: this._ref,
                // Extend visibility outside of the viewport
                rootMargin: `${SkeletonDetectionBoundary}px`,
                threshold: visibilityThresholds,
            });
        this._skeletonObserver2 = new IntersectionObserver(
            this.onSkeletonVisibilityChange,
            {
                root: this._ref,
                // Extend visibility outside of the viewport
                rootMargin: `${2*SkeletonDetectionBoundary}px`,
                threshold: visibilityThresholds,

            });

        this._ironPantsIntervalHandle = self.setInterval(this.onIronPantsHandle, IronPantsHandlePeriod);

        this._unmeasuredItems = new Set<string>();
        this._visibleItems = new Set<string>();

        this._skeletonObserver0.observe(this._spacerRef);
        this._skeletonObserver0.observe(this._endSpacerRef);
        this._skeletonObserver1.observe(this._spacerRef);
        this._skeletonObserver1.observe(this._endSpacerRef);
        this._skeletonObserver2.observe(this._spacerRef);
        this._skeletonObserver2.observe(this._endSpacerRef);

        this._items = new Map<string, VirtualListItem>();
        this._renderState = {
            renderIndex: -1,
            query: VirtualListDataQuery.None,

            spacerSize: 0,
            endSpacerSize: 0,
            startExpansion: 0,
            endExpansion: 0,
            hasVeryFirstItem: false,
            hasVeryLastItem: false,

            scrollToKey: null,
        };

        this.maybeOnRenderEnd([], this._renderEndObserver);
    };

    public dispose() {
        this._isDisposed = true;
        this._abortController.abort();
        this._renderEndObserver.disconnect();
        this._skeletonObserver0.disconnect();
        this._skeletonObserver1.disconnect();
        this._skeletonObserver2.disconnect();
        this._visibilityObserver.disconnect();
        this._scrollPivotObserver.disconnect();
        this._sizeObserver.disconnect();
        this._whenRenderCompleted?.resolve(undefined);
        this._whenUpdateCompleted?.resolve(undefined);
        clearInterval(this._ironPantsIntervalHandle);
        this._ref.removeEventListener('scroll', this.onScroll);
    }

    private get hasUnmeasuredItems(): boolean {
        return this._unmeasuredItems.size > 0 || !this._orderedItems;
    }

    private get fullRange(): NumberRange | null {
        return this._itemRange == null
            ? null
            : new NumberRange(
                this._itemRange.Start - this._renderState.spacerSize,
                this._itemRange.End + this._renderState.endSpacerSize);
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private maybeOnRenderEnd = (mutations: MutationRecord[], _observer: MutationObserver): void => {
        this._isRendering = true;
        debugLog?.log(`maybeOnRenderEnd: `, mutations.length);

        // request recalculation of the item range as we've got new items
        this._shouldRecalculateItemRange = true;
        this._whenRenderCompleted?.resolve(undefined);
        this._whenRenderCompleted = new PromiseSource<void>();

        let isNodesAdded = mutations.length == 0; // first render
        for (const mutation of mutations) {
            if (mutation.type === 'childList') {
                for (const node of mutation.removedNodes) {
                    if (!node['dataset'])
                        continue;

                    const itemRef = node as HTMLElement;
                    const key = getItemKey(itemRef);
                    this._items.delete(key);
                    this._unmeasuredItems.delete(key);
                    this._visibleItems.delete(key);
                    this._sizeObserver.unobserve(itemRef);
                    this._visibilityObserver.unobserve(itemRef);
                    this._scrollPivotObserver.unobserve(itemRef);
                }

                // iterating over all mutations addedNodes is slow
                isNodesAdded ||= mutation.addedNodes.length > 0;
            }
        }

        if (isNodesAdded) {
            for (const itemRef of this.getNewItemRefs()) {
                const key = getItemKey(itemRef);
                const countAs = getItemCountAs(itemRef);

                if (this._items.has(key)) {
                    itemRef.classList.remove('new');
                    continue;
                }

                this._items.set(key, new VirtualListItem(key,countAs ?? 1));
                this._unmeasuredItems.add(key);
                this._sizeObserver.observe(itemRef, { box: 'border-box' });
                this._visibilityObserver.observe(itemRef);
                this._scrollPivotObserver.observe(itemRef);
            }
        }

        if (this._unmeasuredItems.size === 0) {
            for (const itemRef of this.getAllItemRefs()) {
                const key = getItemKey(itemRef);
                const countAs = getItemCountAs(itemRef);

                if (this._items.has(key)) {
                    continue;
                }

                this._items.set(key, new VirtualListItem(key,countAs ?? 1));
                this._unmeasuredItems.add(key);
                this._sizeObserver.observe(itemRef, { box: 'border-box' });
                this._visibilityObserver.observe(itemRef);
                this._scrollPivotObserver.observe(itemRef);
            }
        }

        requestAnimationFrame(time => {
            // make rendered items visible
            const orderedItems = new Array<VirtualListItem>();
            for (const itemRef of this.getNewItemRefs()) {
                itemRef.classList.remove('new');
            }
            // store item order
            for (const itemRef of this.getAllItemRefs()) {
                const key = getItemKey(itemRef);
                const item = this._items.get(key);
                if (item) {
                    orderedItems.push(item);
                }
            }
            this._orderedItems = orderedItems;

            const rs = this.getRenderState();
            if (rs) {
                void this.onRenderEnd(rs);
            }
            else {
                this._isRendering = false;
            }
        });
    };

    private onResize = (entries: ResizeObserverEntry[], observer: ResizeObserver): void => {
        let itemsWereMeasured = false;
        for (const entry of entries) {
            const contentBoxSize = Array.isArray(entry.contentBoxSize)
                ? entry.contentBoxSize[0]
                : entry.contentBoxSize;

            const key = getItemKey(entry.target as HTMLElement);
            const hasRemoved = this._unmeasuredItems.delete(key);
            itemsWereMeasured ||= hasRemoved;

            const item = this._items.get(key);
            if (item) {
                item.size = contentBoxSize.blockSize;
            }
        }
        const lastItemWasMeasured = itemsWereMeasured && this._unmeasuredItems.size == 0;
        if (lastItemWasMeasured)
            this.updateViewportThrottled();

        // recalculate item range as some elements were updated
        this._shouldRecalculateItemRange = true;
    };

    private onItemVisibilityChange = (entries: IntersectionObserverEntry[], _observer: IntersectionObserver): void => {
        let hasChanged = false;
        for (const entry of entries) {
            const itemRef = entry.target as HTMLElement;
            const key = getItemKey(itemRef);
            if (entry.intersectionRatio <= 0.2 && !entry.isIntersecting) {
                hasChanged ||= this._visibleItems.has(key);
                this._visibleItems.delete(key);
            }
            else if (entry.intersectionRatio >= 0.4 && entry.isIntersecting) {
                hasChanged ||= !this._visibleItems.has(key);
                this._visibleItems.add(key);
            }

            this._top = entry.rootBounds.top + VisibilityEpsilon;
        }
        if (hasChanged) {
            const rs = this._renderState;
            let hasStickyEdge = false;
            if (rs.hasVeryLastItem) {
                const edgeKey = this.getLastItemKey();
                if (this._visibleItems.has(edgeKey)) {
                    this.setStickyEdge({ itemKey: edgeKey, edge: VirtualListEdge.End });
                    hasStickyEdge = true;
                }
            }
            if (!hasStickyEdge && rs.hasVeryFirstItem) {
                const edgeKey = this.getFirstItemKey();
                if (this._visibleItems.has(edgeKey)) {
                    this.setStickyEdge({ itemKey: edgeKey, edge: VirtualListEdge.Start });
                    hasStickyEdge = true;
                }
            }
            if (!hasStickyEdge && this._stickyEdge !== null) {
                this.setStickyEdge(null);
            }

            if (!this._isRendering) {
                const location = window.location;
                const now = Date.now();
                const timeSinceLastScroll = now - this._scrollTime ?? 0;
                // longest scroll animation at Chrome is 3s
                if (location.hash !== '' && timeSinceLastScroll > 3000) {
                    const scrollToKey = location.hash.substring(1);
                    if (!this._visibleItems.has(scrollToKey)) {
                        const currentState = history.state;
                        history.pushState(currentState, document.title, location.pathname + location.search);
                    }
                }
            }

            this.updateVisibleKeysThrottled();
        }
    };

    private onScrollPivotVisibilityChange = (entries: IntersectionObserverEntry[], observer: IntersectionObserver): void => {
        if (this._isRendering)
            return;

        this._pivots = entries
            .map(entry => {
                const pivot: Pivot = {
                    itemKey: getItemKey(entry.target as HTMLElement),
                    offset: entry.boundingClientRect.top,
                };
                return pivot;
            });
    };

    private onSkeletonVisibilityChange = (entries: IntersectionObserverEntry[], observer: IntersectionObserver): void => {
        let isNearSkeleton = false;
        for (const entry of entries) {
            isNearSkeleton ||= entry.isIntersecting
                && entry.boundingClientRect.height > EdgeEpsilon;
        }
        if (isNearSkeleton)
            this._isNearSkeleton = isNearSkeleton;
        else
            this.turnOffIsNearSkeletonDebounced();
        // debug helper
        // console.warn("skeleton triggered");
    };

    private turnOffIsNearSkeletonDebounced = debounce(() => this.turnOffIsNearSkeleton(), ScrollDebounce, true);
    private turnOffIsNearSkeleton() {
        this._isNearSkeleton = false;
    }

    private getRenderState(): VirtualListRenderState | null {
        const rsJson = this._renderStateRef.textContent;
        if (rsJson == null || rsJson === '')
            return null;

        const rs = JSON.parse(rsJson) as Required<VirtualListRenderState>;
        if (rs.renderIndex <= this._renderState.renderIndex)
            return null;

        const riText = this._renderIndexRef.dataset['renderIndex'];
        if (riText == null || riText == '')
            return null;

        const ri = Number.parseInt(riText);
        if (ri != rs.renderIndex)
            return null;

        return rs;
    }

    private async onRenderEnd(rs: VirtualListRenderState): Promise<void> {
        debugLog?.log(`onRenderEnd, renderIndex = #${rs.renderIndex}, rs =`, rs);

        try {
            this._renderState = rs;

            // Update statistics
            const ratio = this._statistics.responseFulfillmentRatio;
            if (rs.query.expandStartBy > 0 && !rs.hasVeryFirstItem)
                this._statistics.addResponse(rs.startExpansion, rs.query.expandStartBy * ratio);
            if (rs.query.expandEndBy > 0 && !rs.hasVeryLastItem)
                this._statistics.addResponse(rs.endExpansion, rs.query.expandEndBy * ratio);

            const scrollToItemRef = this.getItemRef(rs.scrollToKey);
            if (scrollToItemRef != null) {
                // Server-side scroll request
                if (!this.isItemFullyVisible(scrollToItemRef)) {
                    if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                        this.scrollTo(scrollToItemRef, false, 'end');
                        this.setStickyEdge({ itemKey: rs.scrollToKey, edge: VirtualListEdge.End });
                    } else {
                        this.scrollTo(scrollToItemRef, false, 'center');
                    }
                }
                else if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                    this.setStickyEdge({ itemKey: rs.scrollToKey, edge: VirtualListEdge.End });
                }
            } else if (this._stickyEdge != null) {
                // Sticky edge scroll
                const itemKey = this._stickyEdge?.edge === VirtualListEdge.Start && rs.hasVeryFirstItem
                    ? this.getFirstItemKey()
                    : this._stickyEdge?.edge === VirtualListEdge.End && rs.hasVeryLastItem
                        ? this.getLastItemKey()
                        : null;
                if (itemKey == null) {
                    this.setStickyEdge(null);
                } else {
                    this.setStickyEdge({ itemKey: itemKey, edge: this._stickyEdge.edge });
                    // scroll is required for start edge only - the list is reverse-rendered
                    if (this._stickyEdge?.edge === VirtualListEdge.Start) {
                        let itemRef = this.getItemRef(itemKey);
                        this.scrollTo(itemRef, false);
                    }
                }
            }
            else if (this._pivots?.length) {
                // do not resync if the skeletons are far away
                if (!this._isNearSkeleton && this._scrollDirection !== 'down')
                    return;

                for (const pivot of this._pivots) {
                    // resync scroll to make pivot ref position the same within viewport
                    const pivotRef = this.getItemRef(pivot.itemKey);
                    if (!pivotRef)
                        continue;

                    const pivotOffset = pivot.offset;
                    const itemRect = pivotRef.getBoundingClientRect();
                    const currentPivotOffset = itemRect.top;
                    const dPivotOffset = pivotOffset - currentPivotOffset;
                    if (Math.abs(dPivotOffset) > PivotSyncEpsilon) {
                        debugLog?.log(`onRenderEnd: resync [${pivot.itemKey}]: ${pivotOffset} ~> ${itemRect.top} + ${dPivotOffset}`);
                        // debug helper
                        // pivotRef.style.backgroundColor = `rgb(${Math.random() * 255},${Math.random() * 255},${Math.random() * 255})`;
                        this._ref.scrollTop -= dPivotOffset;
                    } else {
                        debugLog?.log(`onRenderEnd: resync skipped [${pivot.itemKey}]: ${pivotOffset} ~ ${itemRect.top}`);
                    }
                    break;
                }
            }
        } finally {
            this._isRendering = false;
            this._whenRenderCompleted?.resolve(undefined);
            this._whenUpdateCompleted?.resolve(undefined);

            // skeleton time to time become visible after render and scroll
            if (this._itemRange && this._viewport) {
                const isNearSkeleton =
                    this._renderState.spacerSize > 0 && this._viewport.Start - this._itemRange.Start < 2 * SkeletonDetectionBoundary
                    || this._renderState.endSpacerSize > 0 && this._itemRange.End - this._viewport.End < 2 * SkeletonDetectionBoundary;
                // only turn this flag on, will be cleared off with debounce at onSkeletonVisibilityChange
                if (isNearSkeleton) {
                    this._isNearSkeleton = isNearSkeleton;
                    // debug helper
                    // console.warn("manual near skeleton");
                }
                else {
                    this.turnOffIsNearSkeletonDebounced();
                }
            }

            // trigger update only for first render to load data if needed
            if (rs.renderIndex <= 1)
                void this.updateViewport();
        }
    }

    private updateViewportThrottled = throttle(() => this.updateViewport(), UpdateViewportInterval, 'delayHead');
    private updateViewport = serialize(async () => {
        const rs = this._renderState;
        if (this._isDisposed || this._isRendering)
            return;

        // do not update client state when we haven't completed rendering for the first time
        if (rs.renderIndex === -1)
            return;

        const viewport = await new Promise<NumberRange | null>(resolve => {
            requestAnimationFrame(time => {
                const viewportHeight = this._ref.clientHeight;
                const scrollHeight = this._ref.scrollHeight;
                const scrollTop = this._ref.scrollTop + scrollHeight - viewportHeight;
                const clientViewport = new NumberRange(scrollTop, scrollTop + viewportHeight);
                let viewport: NumberRange | null = null;
                const fullRange = this.fullRange;
                if (fullRange != null) {
                    viewport = clientViewport.fitInto(fullRange);
                }
                resolve(viewport);
            });
        });
        // update item range
        this.ensureItemRangeCalculated();

        debugLog?.log(`updateViewport: `, viewport);

        if (this._viewport && viewport) {
            if (viewport.Start < this._viewport.Start)
                this._scrollDirection = 'up';
            else
                this._scrollDirection = 'down';
        }

        this._viewport = viewport;
        await this.requestData();
    }, 2);

    private updateVisibleKeysThrottled = throttle(() => this.updateVisibleKeys(), UpdateVisibleKeysInterval, 'delayHead');
    private updateVisibleKeys = serialize(async () => {
        if (this._isDisposed)
            return;

        const visibleKeys = [...this._visibleItems].sort();
        await this._blazorRef.invokeMethodAsync('UpdateVisibleKeys', visibleKeys);
    }, 2);

    // Event handlers

    private onIronPantsHandle = (): void => {
        // check if mutationObserver is stuck
        const mutations = this._renderEndObserver.takeRecords();
        if (mutations.length > 0) {
            debugLog?.log(`onIronPantsHandle: iron pants rock!`);
            this.maybeOnRenderEnd(mutations, this._renderEndObserver);
        }
        // else if (this._unmeasuredItems.size > 0) {
        //     // force size calculation resubscribe
        //     this.maybeOnRenderEnd([], this._renderEndObserver);
        // }
        const intersections = this._visibilityObserver.takeRecords();
        if (intersections.length > 0) {
            this.onItemVisibilityChange(intersections, this._visibilityObserver);
        }
        if (this._isNearSkeleton) {
            void this.updateViewportThrottled();
        }
    };

    private onScroll = (): void => {
        this._isScrolling = true;
        this.turnOffIsScrollingDebounced();
        if (this._isRendering || this._isDisposed)
            return;

        this.updateViewportThrottled();
    };

    private turnOffIsScrollingDebounced = debounce(() => this.turnOffIsScrolling(), ScrollDebounce, true);
    private turnOffIsScrolling() {
        this._isScrolling = false;
        this._scrollDirection = 'none';
    }

    private getNewItemRefs(): IterableIterator<HTMLElement> {
        // getElementsByClassName is faster than querySelectorAll
        return Array.from(this._containerRef.getElementsByClassName('item new')).values() as IterableIterator<HTMLElement>;
    }

    private getAllItemRefs(): IterableIterator<HTMLElement> {
        // getElementsByClassName is faster than querySelectorAll
        return Array.from(this._containerRef.getElementsByClassName('item')).values() as IterableIterator<HTMLElement>;
    }

    private getItemRef(key: string): HTMLElement | null {
        if (key == null || key == '')
            return null;

        return this._containerRef.querySelector(`:scope > .item[data-key="${key}"]`);
    }

    private getFirstItemRef(): HTMLElement | null {
        const itemRef = this._containerRef.firstElementChild;
        if (itemRef == null || !itemRef.classList.contains('item'))
            return null;
        return itemRef as HTMLElement;
    }

    private getFirstItemKey(): string | null {
        return getItemKey(this.getFirstItemRef());
    }

    private getLastItemRef(): HTMLElement | null {
        const itemRef = this._containerRef.lastElementChild;
        if (itemRef == null || !itemRef.classList.contains('item'))
            return null;
        return itemRef as HTMLElement;
    }

    private getLastItemKey(): string | null {
        return getItemKey(this.getLastItemRef());
    }

    private isItemFullyVisible(itemRef: HTMLElement): boolean {
        const itemRect = itemRef.getBoundingClientRect();
        const viewRect = this._ref.getBoundingClientRect();
        return itemRect.top >= viewRect.top && itemRect.top <= viewRect.bottom
            && itemRect.bottom >= viewRect.top && itemRect.bottom <= viewRect.bottom
            && itemRect.height > 0;
    }

    private scrollTo(
        itemRef?: HTMLElement,
        useSmoothScroll: boolean = false,
        blockPosition: ScrollLogicalPosition = 'nearest') {
        debugLog?.log(`scrollTo, item key:`, getItemKey(itemRef));
        this._scrollTime = Date.now();
        itemRef?.scrollIntoView({
            behavior: useSmoothScroll ? 'smooth' : 'auto',
            block: blockPosition,
            inline: 'nearest',
        });
    }

    private setStickyEdge(stickyEdge: VirtualListStickyEdgeState): boolean {
        const old = this._stickyEdge;
        if (old?.itemKey !== stickyEdge?.itemKey || old?.edge !== stickyEdge?.edge) {
            debugLog?.log(`setStickyEdge:`, stickyEdge);
            this._stickyEdge = stickyEdge;
            return true;
        }
        return false;
    }

    private ensureItemRangeCalculated(): boolean {
        // nothing to do when unmeasured items still exist or there were no new renders
        if (this.hasUnmeasuredItems || (!this._shouldRecalculateItemRange && this._itemRange))
            return false;

        // nothing to do when there are no items rendered
        if ((this._orderedItems?.length ?? 0) == 0)
            return;

        const orderedItems = this._orderedItems;
        let cornerStoneItemIndex = orderedItems.length - 1;
        let cornerStoneItem = orderedItems[cornerStoneItemIndex];
        for (let i = 0; i < orderedItems.length; i++) {
            const item = orderedItems[i];
            if (!item.range)
                continue;

            if (!(cornerStoneItem?.range) || cornerStoneItem?.range.End > item.range.End) {
                cornerStoneItem = item;
                cornerStoneItemIndex = i;
            }
        }
        if (!cornerStoneItem.range) {
            cornerStoneItem.range = new NumberRange(-cornerStoneItem.size, 0);
        }
        let prevItem = cornerStoneItem;
        for (let i = cornerStoneItemIndex + 1; i < orderedItems.length; i++) {
            const item = orderedItems[i];
            item.range = new NumberRange(prevItem.range.End, prevItem.range.End + item.size);
            prevItem = item;
        }
        prevItem = cornerStoneItem;
        for (let i = cornerStoneItemIndex - 1; i >= 0; i--) {
            const item = orderedItems[i];
            item.range = new NumberRange(prevItem.range.Start - item.size, prevItem.range.Start);
            prevItem = item;
        }
        this._itemRange = new NumberRange(
            orderedItems[0].range.Start,
            orderedItems[0].range.Start + orderedItems.map(it => it.size).reduce((sum, curr) => sum + curr, 0));

        this._shouldRecalculateItemRange = false;
        return true;
    }

    private async requestData(): Promise<void> {
        if (this._isRendering || !this._viewport || !this._itemRange)
            return;

        this._query = this.getDataQuery();
        if (!this.dataRequestIsRequired(this._query) && !this._isNearSkeleton)
            return;
        if (this._query.isNone)
            return;

        debugLog?.log(`requestData: query:`, this._query);

        this._whenUpdateCompleted = new PromiseSource<void>();

        // debug helper
        // await delayAsync(50);
        await this._blazorRef.invokeMethodAsync('RequestData', this._query);
        this._lastQuery = this._query;
        this._lastQueryTime = Date.now();
    }

    private dataRequestIsRequired(query: VirtualListDataQuery): boolean
    {
        const currentItemRange = this._itemRange;
        const queryItemRange = query.virtualRange;
        if (!currentItemRange || !queryItemRange)
            return false;

        if (!this._viewport)
            return false;

        if (this._query === query)
            return false;

        const viewportSize = this._viewport.size;
        const intersection = currentItemRange.intersectWith(queryItemRange);
        if (intersection.isEmpty)
            return true;

        return (Math.abs(queryItemRange.Start - intersection.Start) > viewportSize / 2)
            || (Math.abs(queryItemRange.End - intersection.End) > viewportSize / 2);
    }

    private getDataQuery(): VirtualListDataQuery {
        const rs = this._renderState;
        const itemSize = this._statistics.itemSize;
        const responseFulfillmentRatio = this._statistics.responseFulfillmentRatio;
        const viewport = this._viewport;
        const alreadyLoaded = this._itemRange;
        if (!viewport || !alreadyLoaded)
            return this._lastQuery;

        const loadZoneSize = viewport.size * 3;
        let loadStart = viewport.Start - loadZoneSize;
        if (loadStart < alreadyLoaded.Start && rs.hasVeryFirstItem)
            loadStart = alreadyLoaded.Start;
        let loadEnd = viewport.End + loadZoneSize;
        if (loadEnd > alreadyLoaded.End && rs.hasVeryLastItem)
            loadEnd = alreadyLoaded.End;
        let bufferZoneSize = loadZoneSize * 3;
        const loadZone = new NumberRange(loadStart, loadEnd);
        const bufferZone = new NumberRange(
            viewport.Start - bufferZoneSize,
            viewport.End + bufferZoneSize);

        if (this.hasUnmeasuredItems) // Let's wait for measurement to complete first
            return this._lastQuery;
        if (this._items.size == 0) // No entries -> nothing to "align" the query to
            return this._lastQuery;
        if (alreadyLoaded.contains(loadZone)) {
            // debug helper
            // console.warn('already!', viewport, alreadyLoaded, loadZone);
            return this._lastQuery;
        }

        let startIndex = -1;
        let endIndex = -1;
        const items = this._orderedItems;
        for (let i = 0; i < items.length; i++) {
            const item = items[i];
            if (item.isMeasured && item.range.intersectWith(bufferZone).size > 0) {
                endIndex = i;
                if (startIndex < 0)
                    startIndex = i;
            } else if (startIndex >= 0)
                break;
        }
        if (startIndex < 0) {
            // No items inside the bufferZone, so we'll take the first or the last item
            startIndex = endIndex = items[0].range.End < bufferZone.Start
                ? 0
                : items.length - 1;
        }

        const firstItem = items[startIndex];
        const lastItem = items[endIndex];
        const startGap = Math.max(0, firstItem.range.Start - loadZone.Start);
        const endGap = Math.max(0, loadZone.End - lastItem.range.End);
        const expandStartBy = this._renderState.hasVeryFirstItem || startGap === 0
            ? 0
            : clamp(Math.ceil(Math.max(startGap, loadZoneSize) / itemSize), 0, MaxExpandBy);
        const expandEndBy = this._renderState.hasVeryLastItem || endGap === 0
            ? 0
            : clamp(Math.ceil(Math.max(endGap, loadZoneSize) / itemSize), 0, MaxExpandBy);
        const keyRange = new Range(firstItem.key, lastItem.key);
        const query = new VirtualListDataQuery(keyRange, loadZone);
        query.expandStartBy = expandStartBy / responseFulfillmentRatio;
        query.expandEndBy = expandEndBy / responseFulfillmentRatio;

        if (query.expandStartBy === query.expandEndBy && query.expandEndBy === 0)
            return this._lastQuery;

        return query;
    }
}

// Helper functions
function getItemKey(itemRef?: HTMLElement): string | null {
    return itemRef?.dataset['key'];
}

function getItemCountAs(itemRef?: HTMLElement): number | null {
    if (itemRef == null)
        return null;

    const countString = itemRef.dataset['countAs'];
    if (countString == null)
        return null;``;

    return parseInt(countString);
}

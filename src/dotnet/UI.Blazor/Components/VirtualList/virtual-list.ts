import { debounce, PromiseSource, PromiseSourceWithTimeout, serialize, throttle } from 'promises';
import { clamp } from 'math';
import { NumberRange, Range } from './ts/range';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListStickyEdgeState } from './ts/virtual-list-sticky-edge-state';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { VirtualListItem } from './ts/virtual-list-item';
import { VirtualListStatistics } from './ts/virtual-list-statistics';
import { Pivot } from './ts/pivot';

import { Log } from 'logging';

const { debugLog } = Log.get('VirtualList');

const UpdateViewportInterval: number = 320;
const UpdateItemVisibilityInterval: number = 250;
const IronPantsHandlePeriod: number = 1600;
const PivotSyncEpsilon: number = 16;
const VisibilityEpsilon: number = 4;
const EdgeEpsilon: number = 4;
const MaxExpandBy: number = 320;
const ScrollDebounce: number = 600;
const RemoveOldItemsDebounce: number = 2000;
const SkeletonDetectionBoundary: number = 200;
const MinViewPortSize: number = 400;
const UpdateTimeout: number = 800;

export class VirtualList {
    /** ref to div.virtual-list */
    private readonly _ref: HTMLElement;
    private readonly _containerRef: HTMLElement;
    private readonly _renderStateRef: HTMLElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _spacerRef: HTMLElement;
    private readonly _endSpacerRef: HTMLElement;
    private readonly _renderIndexRef: HTMLElement;
    private readonly _endAnchorRef: HTMLElement;
    private readonly _abortController: AbortController;
    private readonly _renderEndObserver: MutationObserver;
    private readonly _sizeObserver: ResizeObserver;
    private readonly _visibilityObserver: IntersectionObserver;
    private readonly _scrollPivotObserver: IntersectionObserver;
    private readonly _skeletonObserver0: IntersectionObserver;
    private readonly _skeletonObserver1: IntersectionObserver;
    private readonly _ironPantsIntervalHandle: number;
    private readonly _unmeasuredItems: Set<string>;
    private readonly _visibleItems: Set<string>;
    private readonly _items: Map<string, VirtualListItem>;
    private readonly _itemRefs: Array<HTMLLIElement> = [];
    private readonly _newItemRefs: Array<HTMLLIElement> = [];
    private readonly _statistics: VirtualListStatistics = new VirtualListStatistics();
    private readonly _keySortCollator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

    private _isDisposed = false;
    private _stickyEdge: Required<VirtualListStickyEdgeState> | null = null;
    private _whenRenderCompleted: PromiseSource<void> | null = null;
    private _whenUpdateCompleted: PromiseSource<void> | null = null;
    private _pivots: Pivot[] = [];
    private _top: number;
    private _lastVisibleItem: string | null = null;

    private _isRendering: boolean = false;
    private _isNearSkeleton: boolean = false;
    private _isEndAnchorVisible: boolean = false;
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
            debugLog?.log(`constructor`);
            globalThis['virtualList'] = this;
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
        this._endAnchorRef = this._ref.querySelector(':scope > .end-anchor');

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
                threshold: [0, 1],
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

        this._ironPantsIntervalHandle = self.setInterval(this.onIronPantsHandle, IronPantsHandlePeriod);

        this._unmeasuredItems = new Set<string>();
        this._visibleItems = new Set<string>();

        this._visibilityObserver.observe(this._endAnchorRef);
        this._skeletonObserver0.observe(this._spacerRef);
        this._skeletonObserver0.observe(this._endSpacerRef);
        this._skeletonObserver1.observe(this._spacerRef);
        this._skeletonObserver1.observe(this._endSpacerRef);

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
                this._itemRange.start - this._renderState.spacerSize,
                this._itemRange.end + this._renderState.endSpacerSize);
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private maybeOnRenderEnd = (mutations: MutationRecord[], _observer: MutationObserver): void => {
        this._isRendering = true;

        this._itemRefs.fill(null);
        this._newItemRefs.fill(null);

        const removedCount = mutations.reduce((prev, m) => prev+ m.removedNodes.length, 0);
        const addedCount = mutations.reduce((prev, m) => prev+ m.addedNodes.length, 0);
        const queryDuration = Math.max(0, Date.now() - this._lastQueryTime ?? 0);
        debugLog?.log(
            `maybeOnRenderEnd: query duration: `,
            queryDuration,
            '; added: ',
            addedCount,
            '; removed: ',
            removedCount);

        // request recalculation of the item range as we've got new items
        this._shouldRecalculateItemRange = true;
        this._whenRenderCompleted?.resolve(undefined);
        this._whenRenderCompleted = new PromiseSource<void>();

        // let isNodesAdded = mutations.length == 0; // first render
        for (const mutation of mutations) {
            if (mutation.type !== 'childList') { continue; }
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
            for (const node of mutation.addedNodes) {
                const itemRef = node as HTMLElement;
                const key = getItemKey(itemRef);
                if (!key)
                    continue;

                itemRef.classList.remove('new');
                if (this._items.has(key)) {
                    const item = this._items.get(key);
                    item.isOld = false;
                    continue;
                }

                const newItem = this.createListItem(key, itemRef);
                this._items.set(key, newItem);
            }
        }

        // make rendered items visible
        for (const itemRef of this.getNewItemRefs()) {
            itemRef.classList.remove('new');
        }

        this.updateOrderedItems();

        const rs = this.getRenderState();
        if (rs) {
            void this.onRenderEnd(rs);
        }
        else {
            this._isRendering = false;
        }
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
        const rs = this._renderState;
        for (const entry of entries) {
            const itemRef = entry.target as HTMLElement;
            const key = getItemKey(itemRef);
            if (!key) {
                if (this._endAnchorRef === itemRef) {
                    if (entry.isIntersecting) {
                        hasChanged ||= !this._isEndAnchorVisible;
                        this._isEndAnchorVisible = true;
                        if (rs.hasVeryLastItem) {
                            const edgeKey = this.getLastItemKey();
                            this.setStickyEdge({ itemKey: edgeKey, edge: VirtualListEdge.End });
                        }
                        this.turnOffIsEndAnchorVisibleDebounced.reset();
                    }
                    else if (this._isEndAnchorVisible) {
                        this.turnOffIsEndAnchorVisibleDebounced();
                    }
                }
                continue;
            }
            if (entry.intersectionRatio <= 0.2 && !entry.isIntersecting) {
                hasChanged ||= this._visibleItems.has(key);
                this._visibleItems.delete(key);
            }
            else if ((entry.intersectionRatio >= 0.4 || entry.intersectionRect.height > MinViewPortSize / 2) && entry.isIntersecting) {
                hasChanged ||= !this._visibleItems.has(key);
                this._visibleItems.add(key);
            }

            this._lastVisibleItem = key;
            this._top = entry.rootBounds.top + VisibilityEpsilon;
        }
        if (hasChanged) {
            let hasStickyEdge = false;
            if (rs.hasVeryLastItem) {
                const edgeKey = this.getLastItemKey();
                if (this._visibleItems.has(edgeKey) || this._isEndAnchorVisible) {
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

            this.updateVisibleKeysThrottled();
        }
    };

    private onScrollPivotVisibilityChange = (entries: IntersectionObserverEntry[], observer: IntersectionObserver): void => {
        if (this._isRendering)
            return;

        const viewportSize = this._viewport?.size ?? MinViewPortSize;
        const relativeViewport = new NumberRange(-viewportSize/2, 1.5*viewportSize);
        this._pivots = entries
            .filter(entry => relativeViewport.contains(entry.boundingClientRect.top))
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
        if (isNearSkeleton) {
            this._isNearSkeleton = isNearSkeleton;
            // reset turn off attempt
            this.turnOffIsNearSkeletonDebounced.reset();
            this.turnOffIsNearSkeletonDebounced();
            this.updateViewportThrottled();
        }
        else
            this.turnOffIsNearSkeletonDebounced();
        // debug helper
        // console.warn("skeleton triggered", isNearSkeleton);
    };

    private turnOffIsNearSkeletonDebounced = debounce(() => this.turnOffIsNearSkeleton(), ScrollDebounce, true);
    private turnOffIsNearSkeleton() {
        this._isNearSkeleton = false;
    }

    private turnOffIsEndAnchorVisibleDebounced = debounce(() => this.turnOffIsEndAnchorVisible(), ScrollDebounce, true);
    private turnOffIsEndAnchorVisible() {
        this._isEndAnchorVisible = false;
        if (this._stickyEdge?.edge === VirtualListEdge.End) {
            this.setStickyEdge(null);
        }

        this.updateVisibleKeysThrottled();
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
                        this.scrollToEnd(false);
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
                    // let's scroll to the latest edge key when we've got a lot of new messages
                    if (this._stickyEdge?.edge === VirtualListEdge.End) {
                        let itemRef = this.getItemRef(this._stickyEdge.itemKey);
                        this.scrollTo(itemRef, false, 'end');
                    }
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
                for (const pivot of this._pivots) {
                    // resync scroll to make pivot ref position the same within viewport
                    const pivotRef = this.getItemRef(pivot.itemKey);
                    if (!pivotRef)
                        continue;

                    new Promise<void>(resolve => {
                        requestAnimationFrame(() => {
                            const pivotOffset = pivot.offset;
                            const itemRect = pivotRef.getBoundingClientRect();
                            const currentPivotOffset = itemRect.top;
                            const dPivotOffset = pivotOffset - currentPivotOffset;
                            if (Math.abs(dPivotOffset) > PivotSyncEpsilon) {
                                debugLog?.log(`onRenderEnd: resync [${pivot.itemKey}]: ${pivotOffset} ~> ${itemRect.top} + ${dPivotOffset}`, pivot);
                                // debug helper
                                // pivotRef.style.backgroundColor = `rgb(${Math.random() * 255},${Math.random() * 255},${Math.random() * 255})`;
                                this._ref.scrollTop -= dPivotOffset;
                            } else {
                                debugLog?.log(`onRenderEnd: resync skipped [${pivot.itemKey}]: ${pivotOffset} ~ ${itemRect.top}`, pivot);
                            }
                            resolve();
                        });
                    });

                    break;
                }
            }
        } finally {
            this._isRendering = false;
            this._whenRenderCompleted?.resolve(undefined);
            this._whenUpdateCompleted?.resolve(undefined);

            // trigger update only for first render to load data if needed
            if (rs.renderIndex <= 1)
                void this.updateViewport();
        }
    }

    private readonly updateViewportThrottled = throttle((getRidOfOldItems?: boolean) => this.updateViewport(getRidOfOldItems), UpdateViewportInterval, 'delayHead');
    private readonly updateViewport = serialize(async (getRidOfOldItems?: boolean) => {
        const rs = this._renderState;
        if (this._isDisposed || this._isRendering)
            return;

        // do not update client state when we haven't completed rendering for the first time
        if (rs.renderIndex === -1)
            return;

        const rangeStarts = new Array<number>();
        const rangeEnds = new Array<number>();
        const visibleItems = this._visibleItems.size > 0
            ? this._visibleItems
            : new Array(1).fill(this._lastVisibleItem);
        for (const key of visibleItems.values()) {
            const item = this._items.get(key);
            if (!item) {
                debugLog?.log('updateViewport: can not find item by visible key:', key)
                continue;
            }
            if (item.isMeasured) {
                rangeStarts.push(item.range.start);
                rangeEnds.push(item.range.end);
            }
        }

        const viewport: NumberRange | null = (!this.fullRange || rangeStarts.length === 0)
            ? null
            : new NumberRange(Math.min(...rangeStarts), Math.max(...rangeEnds));

        // update item range
        const isViewportUnknown = viewport == null;
        if (!this.ensureItemRangeCalculated(isViewportUnknown) && !this._itemRange) {
            this.updateViewportThrottled();
            return;
        }

        if (isViewportUnknown)
            debugLog?.log(`updateViewport: `, null);

        if (this._viewport && viewport) {
            if (viewport.start < this._viewport.start)
                this._scrollDirection = 'up';
            else
                this._scrollDirection = 'down';
        }

        this._viewport = viewport;
        if (!isViewportUnknown)
            await this.requestData(getRidOfOldItems);
        else
            this.updateViewportThrottled();
    }, 2);

    private readonly updateVisibleKeysThrottled = throttle(() => this.updateVisibleKeys(), UpdateItemVisibilityInterval, 'delayHead', 'updateVisibleKeys');
    private readonly updateVisibleKeys = serialize(async () => {
        if (this._isDisposed)
            return;

        const visibleItems = [...this._visibleItems].sort(this._keySortCollator.compare);
        debugLog?.log(`updateVisibleKeys: calling UpdateItemVisibility:`, visibleItems, this._isEndAnchorVisible);
        await this._blazorRef.invokeMethodAsync('UpdateItemVisibility', visibleItems, this._isEndAnchorVisible);
    }, 2);

    private updateOrderedItems(): void {
        const orderedItems = new Array<VirtualListItem>();
        // store item order
        for (const itemRef of this.getAllItemRefs()) {
            const key = getItemKey(itemRef);
            if (!key)
                continue;

            const item = this._items.get(key);
            if (item) {
                orderedItems.push(item);
            } else {
                const newItem = this.createListItem(key, itemRef);
                this._items.set(key, newItem);
                orderedItems.push(newItem);
            }
        }
        this._orderedItems = orderedItems;
    }

    private createListItem(itemKey: string, itemRef: HTMLElement): VirtualListItem {
        const countAs = getItemCountAs(itemRef);
        const newItem = new VirtualListItem(itemKey, countAs ?? 1);
        this._unmeasuredItems.add(itemKey);
        this._sizeObserver.observe(itemRef, { box: 'border-box' });
        this._visibilityObserver.observe(itemRef);
        this._scrollPivotObserver.observe(itemRef);
        return newItem;
    }

    // force repaint to fix blank item rendering issue
    private forceRepaintThrottled = throttle(() => this.forceRepaint(), UpdateItemVisibilityInterval, 'default');
    private forceRepaint(items: HTMLElement[] | null = null): void {
        if (items == null) {
            const visibleItemRefs = new Array<HTMLElement>();
            const visibleItems = this._visibleItems;
            for (let itemKey of visibleItems) {
                const itemRef = this.getItemRef(itemKey);
                if (itemRef) {
                    visibleItemRefs.push(itemRef);
                }
            }
            items = visibleItemRefs;
        }
        else if (items.length <= 0)
            return;

        requestAnimationFrame(() => {
            items.forEach(itemRef => {
                // you can use scale(1) or translate(0, 0), etc
                itemRef.style.setProperty('transform', 'translateZ(0)');
            });

            requestAnimationFrame(() => {
                items.forEach(itemRef => {
                    // this will remove the property 1 frame later
                    itemRef.style.removeProperty('transform');
                });
            });
        });
    }


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
            this.updateViewportThrottled();
        }
    };

    private onScroll = (): void => {
        this._isScrolling = true;
        this.turnOffIsScrollingDebounced();
        this.requestOldItemsRemovalDebounced.reset();
    };

    private turnOffIsScrollingDebounced = debounce(() => this.turnOffIsScrolling(), ScrollDebounce, true);
    private turnOffIsScrolling() {
        this._isScrolling = false;
        this._scrollDirection = 'none';

        // this line below can fix rendering artifacts when some entries are blank
        // but adds significant stutter during scroll
        // this.forceRepaintThrottled();

        if (this._isRendering || this._isDisposed)
            return;

        this.markItemsForRemoval();
    }

    private markItemsForRemoval(): void {
        const viewport = this._viewport;
        const itemRange = this._itemRange;
        if (!viewport || !itemRange)
            return;

        const loadZoneSize = viewport.size;
        let bufferZoneSize = loadZoneSize * 2;
        const bufferZone = new NumberRange(
            viewport.start - bufferZoneSize,
            viewport.end + bufferZoneSize);

        const oldItemsRange = new NumberRange(bufferZone.end, itemRange.end);
        if (oldItemsRange.size <=0)
            return;

        let hasOldItems = false;
        const items = this._orderedItems;
        for (let i = 0; i < items.length; i++) {
            const item = items[i];
            if (!item.range)
                continue;

            if (item.range.intersectWith(oldItemsRange).size > 0) {
                item.isOld = true;
                hasOldItems = true;
            }
        }
        if (hasOldItems)
            this.requestOldItemsRemovalDebounced();
        else
            this.updateViewportThrottled();
    }

    private requestOldItemsRemovalDebounced = debounce(() => this.requestOldItemsRemoval(), RemoveOldItemsDebounce, true);
    private async requestOldItemsRemoval(): Promise<void> {
        const items = this._orderedItems;
        const oldCount = items.reduceRight((prev, item) => (!!item.isOld ? 1 : 0) + prev, 0);
        if (oldCount > 20)
            await this.updateViewportThrottled(true);
    }

    private getNewItemRefs(): HTMLLIElement[] {
        const itemRefs = this._newItemRefs;
        if (itemRefs.length && itemRefs[0])
            return itemRefs;

        const itemRefCollection = this._containerRef.getElementsByClassName('item new') as HTMLCollectionOf<HTMLLIElement>;
        itemRefs.length = itemRefCollection.length;
        for (let i = 0; i < itemRefCollection.length; i++) {
            itemRefs[i] = itemRefCollection[i];
        }
        return itemRefs;
    }

    private getAllItemRefs(): HTMLLIElement[] {
        const itemRefs = this._itemRefs;
        if (itemRefs.length && itemRefs[0])
            return itemRefs;

        const itemRefCollection = this._containerRef.children as HTMLCollectionOf<HTMLLIElement>;
        itemRefs.length = itemRefCollection.length;
        for (let i = 0; i < itemRefCollection.length; i++) {
            itemRefs[i] = itemRefCollection[i];
        }
        return itemRefs;
    }

    private getItemRef(key: string): HTMLElement | null {
        if (key == null)
            return null;

        // return this._containerRef.querySelector(`:scope > .item[data-key="${key}"]`);
        return document.getElementById(key);
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

    private scrollToEnd(
        useSmoothScroll: boolean = false) {
        debugLog?.log('scrollTo end');
        const endAnchor = document.getElementsByClassName('end-anchor')[0] as HTMLElement;
        this.scrollTo(endAnchor, useSmoothScroll, 'end');
    }

    private setStickyEdge(stickyEdge: VirtualListStickyEdgeState | null): boolean {
        const old = this._stickyEdge;
        if (old?.itemKey !== stickyEdge?.itemKey || old?.edge !== stickyEdge?.edge) {
            debugLog?.log(`setStickyEdge:`, stickyEdge);
            this._stickyEdge = stickyEdge;
            return true;
        }
        return false;
    }

    private ensureItemRangeCalculated(isRecalculationForced: boolean): boolean {
        // nothing to do when unmeasured items still exist or there were no new renders
        if (!isRecalculationForced && (this.hasUnmeasuredItems || (!this._shouldRecalculateItemRange && this._itemRange)))
            return false;

        if (this.hasUnmeasuredItems)
            return false;

        if (isRecalculationForced)
            this.updateOrderedItems();

        // nothing to do when there are no items rendered
        if ((this._orderedItems?.length ?? 0) == 0)
            return false;

        const orderedItems = this._orderedItems;
        let cornerStoneItemIndex = orderedItems.length - 1;
        let cornerStoneItem = orderedItems[cornerStoneItemIndex];
        for (let i = 0; i < orderedItems.length; i++) {
            const item = orderedItems[i];
            if (!item.range)
                continue;

            if (!(cornerStoneItem?.range) || cornerStoneItem?.range.end > item.range.end) {
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
            item.range = new NumberRange(prevItem.range.end, prevItem.range.end + item.size);
            prevItem = item;
        }
        prevItem = cornerStoneItem;
        for (let i = cornerStoneItemIndex - 1; i >= 0; i--) {
            const item = orderedItems[i];
            item.range = new NumberRange(prevItem.range.start - item.size, prevItem.range.start);
            prevItem = item;
        }
        this._itemRange = new NumberRange(
            orderedItems[0].range.start,
            orderedItems[0].range.start + orderedItems.map(it => it.size).reduce((sum, curr) => sum + curr, 0));

        this._shouldRecalculateItemRange = false;
        return true;
    }

    private async requestData(getRidOfOldItems: boolean = false): Promise<void> {
        if (this._isRendering || !this._viewport || !this._itemRange)
            return;

        const query = this.getDataQuery(getRidOfOldItems);
        const isDataRequestIsRequired = this.dataRequestIsRequired(query);
        if (!isDataRequestIsRequired && !getRidOfOldItems) {
            return;
        }
        console.log('Data query:', isDataRequestIsRequired, this._isNearSkeleton, getRidOfOldItems);
        if (query.isNone)
            return;

        this._query = query;

        const whenUpdateCompleted = this._whenUpdateCompleted;
        if (whenUpdateCompleted && !whenUpdateCompleted.isCompleted())
            return;

        const newWhenUpdateCompleted = new PromiseSourceWithTimeout<void>();
        newWhenUpdateCompleted.setTimeout(UpdateTimeout, () => {
            newWhenUpdateCompleted.resolve(undefined);
        });
        this._whenUpdateCompleted = newWhenUpdateCompleted;

        if (getRidOfOldItems) {
            const items = this._orderedItems;
            for (let i = 0; i < items.length; i++) {
                const item = items[i];
                item.isOld = false;
            }
        }
        // debug helper
        // await delayAsync(50);
        debugLog?.log(`requestData: query:`, this._query);
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

        return intersection.start - queryItemRange.start > viewportSize
            || queryItemRange.end - intersection.end > viewportSize;
    }

    private getDataQuery(getRidOfOldItems: boolean): VirtualListDataQuery {
        const rs = this._renderState;
        const itemSize = this._statistics.itemSize;
        const responseFulfillmentRatio = this._statistics.responseFulfillmentRatio;
        const viewport = this._viewport;
        const alreadyLoaded = this._itemRange;
        if (!viewport || !alreadyLoaded)
            return this._lastQuery;

        const loadZoneSize = viewport.size * 4;
        let loadStart = viewport.start - loadZoneSize;
        if (loadStart < alreadyLoaded.start && rs.hasVeryFirstItem)
            loadStart = alreadyLoaded.start;
        let loadEnd = viewport.end + loadZoneSize;
        if (loadEnd > alreadyLoaded.end && rs.hasVeryLastItem)
            loadEnd = alreadyLoaded.end;
        let bufferZoneSize = loadZoneSize * 2;
        const loadZone = new NumberRange(loadStart, loadEnd);
        const bufferZone = new NumberRange(
            viewport.start - bufferZoneSize,
            viewport.end + bufferZoneSize);

        if (this.hasUnmeasuredItems) // Let's wait for measurement to complete first
            return this._lastQuery;
        if (this._items.size == 0) // No entries -> nothing to "align" the query to
            return this._lastQuery;
        if (!getRidOfOldItems && alreadyLoaded.contains(loadZone)) {
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
            } else if (startIndex >= 0) {
                if (getRidOfOldItems && item.isOld)
                    break;
                endIndex = i;
            }
        }
        if (startIndex < 0) {
            // No items inside the bufferZone, so we'll take at least 2 of the viewport of existing items
            if (items[0].range.start > loadZone.end) {
                startIndex = endIndex = 0;
                let existingItemsHeight = 0;
                for (let i = 0; i < items.length; i++) {
                    const item = items[i];
                    endIndex = i;
                    existingItemsHeight += item.size;
                    if (existingItemsHeight > viewport.size * 2)
                        break;
                }
            }
            else {
                startIndex = endIndex = items.length - 1;
                let existingItemsHeight = 0;
                for (let i = items.length - 1; i >= 0; i--) {
                    const item = items[i];
                    startIndex = i;
                    existingItemsHeight += item.size;
                    if (existingItemsHeight > viewport.size * 2)
                        break;
                }
            }
        }

        const firstItem = items[startIndex];
        const lastItem = items[endIndex];
        const startGap = Math.max(0, firstItem.range.start - loadZone.start);
        const endGap = Math.max(0, loadZone.end - lastItem.range.end);
        const expandStartBy = this._renderState.hasVeryFirstItem || startGap === 0
            ? 0
            : clamp(Math.ceil(Math.max(startGap, loadZoneSize) / itemSize), 0, MaxExpandBy);
        const expandEndBy = this._renderState.hasVeryLastItem || endGap === 0
            ? 0
            : clamp(Math.ceil(Math.max(endGap, loadZoneSize) / itemSize), 0, MaxExpandBy);
        const keyRange = new Range(firstItem.key, lastItem.key);
        const virtualRange = new NumberRange(firstItem.range.start - startGap, lastItem.range.end + endGap);
        const query = new VirtualListDataQuery(keyRange, virtualRange);
        query.expandStartBy = expandStartBy / responseFulfillmentRatio;
        query.expandEndBy = expandEndBy / responseFulfillmentRatio;

        return query;
    }
}

// Helper functions
function getItemKey(itemRef?: HTMLElement): string | null {
    // return itemRef?.dataset['key'];
    return itemRef?.id;
}

function getItemCountAs(itemRef?: HTMLElement): number {
    if (itemRef == null)
        return null;

    const sCountAs = itemRef.dataset['countAs'];
    return sCountAs == null ? 1 : parseInt(sCountAs);
}

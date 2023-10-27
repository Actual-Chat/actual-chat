import { debounce, delayAsync, PromiseSource, PromiseSourceWithTimeout, serialize, throttle } from 'promises';
import { clamp } from 'math';
import { NumberRange, Range } from './ts/range';
import { InertialScroll } from './ts/inertial-scroll';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListStickyEdgeState } from './ts/virtual-list-sticky-edge-state';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { VirtualListItem } from './ts/virtual-list-item';
import { VirtualListStatistics } from './ts/virtual-list-statistics';
import { Pivot } from './ts/pivot';

import { Log } from 'logging';
import { fastRaf, fastReadRaf, fastWriteRaf } from 'fast-raf';
import { DeviceInfo } from 'device-info';

const { warnLog, debugLog } = Log.get('VirtualList');

const UpdateViewportInterval: number = 320;
const UpdateItemVisibilityInterval: number = 250;
const SafetyTimerPeriod: number = 1600;
const PivotSyncEpsilon: number = 16;
const VisibilityEpsilon: number = 4;
const EdgeEpsilon: number = 4;
const MaxExpandBy: number = 120;
const ScrollDebounce: number = 200;
const SkeletonDetectionBoundary: number = 200;
const MinViewPortSize: number = 400;
const RequestDataTimeout: number = 800;

export class VirtualList {
    /** ref to div.virtual-list */
    private readonly _ref: HTMLElement;
    private readonly _containerRef: HTMLElement;
    private readonly _renderStateRef: HTMLElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _identity: string;
    private readonly _spacerRef: HTMLElement;
    private readonly _endSpacerRef: HTMLElement;
    private readonly _renderIndexRef: HTMLElement;
    private readonly _endAnchorRef: HTMLElement;
    private readonly _inertialScroll: InertialScroll;
    private readonly _abortController: AbortController;
    private readonly _itemSetChangeObserver: MutationObserver;
    private readonly _sizeObserver: ResizeObserver;
    private readonly _visibilityObserver: IntersectionObserver;
    private readonly _skeletonObserver0: IntersectionObserver;
    private readonly _skeletonObserver1: IntersectionObserver;
    private readonly _safetyTimerHandle: number;
    private readonly _unmeasuredItems: Set<string>;
    private readonly _visibleItems: Set<string>;
    private readonly _items: Map<string, VirtualListItem>;
    private readonly _statistics: VirtualListStatistics = new VirtualListStatistics();
    private readonly _keySortCollator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

    private _isDisposed = false;
    private _cachedAllItemRefs: Array<HTMLLIElement> | null = null;
    private _stickyEdge: Required<VirtualListStickyEdgeState> | null = null;
    private _whenRequestDataCompleted: PromiseSource<void> | null = null;
    private _pivots: Pivot[] = [];
    private _top: number;

    private _renderStartedAt: number | null = null;
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
    private _shouldUpdateOrderedItems: boolean = true;

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
    ) {
        return new VirtualList(ref, backendRef, identity);
    }

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
    ) {
        if (debugLog) {
            debugLog?.log(`constructor`);
            globalThis['virtualList'] = this;
        }

        this._ref = ref;
        this._blazorRef = backendRef;
        this._identity = identity;
        this._isDisposed = false;
        this._abortController = new AbortController();
        this._spacerRef = this._ref.querySelector(':scope > .c-spacer-start');
        this._endSpacerRef = this._ref.querySelector(':scope > .c-spacer-end');
        this._containerRef = this._ref.querySelector(':scope > .c-virtual-container');
        this._renderStateRef = this._ref.querySelector(':scope > .data.render-state');
        this._renderIndexRef = this._ref.querySelector(':scope > .data.render-index');
        this._endAnchorRef = this._ref.querySelector(':scope > .c-end-anchor');
        this._inertialScroll = new InertialScroll(this._ref);

        // Events & observers
        const listenerOptions = { signal: this._abortController.signal };
        this._ref.addEventListener('scroll', this.onScroll, listenerOptions);
        this._itemSetChangeObserver = new MutationObserver(this.onItemSetChange);
        this._itemSetChangeObserver.observe(this._containerRef, { childList: true });
        this._itemSetChangeObserver.observe(this._renderIndexRef, { attributes: true });
        this._sizeObserver = new ResizeObserver(this.onResize);
        // An array of numbers between 0.0 and 1.0, specifying a ratio of intersection area to total bounding box area for the observed target.
        // Trigger callbacks as early as it can on any intersection change, even 1 percent
        // 0 threshold doesn't work, despite what is written in the documentation
        const visibilityThresholds = [...Array(101).keys() ].map(i => i / 100);
        this._visibilityObserver = new IntersectionObserver(
            this.onItemVisibilityChange,
            {
                // Track visibility as intersection of virtual list viewport, not the window!
                root: this._ref,
                // Extend visibility outside of the viewport.
                rootMargin: `${VisibilityEpsilon}px`,
                threshold: visibilityThresholds,
            });
        this._skeletonObserver0 = new IntersectionObserver(
            this.onSkeletonVisibilityChange,
            {
                root: this._ref,
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

        this._safetyTimerHandle = self.setInterval(this.onSafetyTimer, SafetyTimerPeriod);

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
            keyRange: new Range<string>('',''),
            spacerSize: 0,
            endSpacerSize: 0,
            requestedStartExpansion: null,
            requestedEndExpansion: null,
            startExpansion: 0,
            endExpansion: 0,
            hasVeryFirstItem: false,
            hasVeryLastItem: false,

            scrollToKey: null,
        };

        // set isRendering as soon as possible
        const origSetAttribute = this._renderIndexRef.setAttribute;
        this._renderIndexRef.setAttribute = (qualifiedName: string, value: string) => {
            // update pivots just before the render
            // we can do this because Blazor updates attributes before changing nodes
            // it's OK to trigger style recalc there - there are no changes made yet
            // we SHOULD NOT fail there - otherwise Blazor will fail
            try {
                this.updateCurrentPivots();
                const time = Date.now();
                debugLog?.log(`renderStartedAt: `, time);
                this._renderStartedAt = time;
                origSetAttribute.call(this._renderIndexRef, qualifiedName, value);
            }
            catch (e) {
                warnLog?.log('renderIndex.setAttribute: failed', e);
            }
        };
        if (this.parseRenderState() === null)
            this._renderStartedAt = Date.now();
        this.onItemSetChange([], this._itemSetChangeObserver);
    };

    public dispose() {
        this._isDisposed = true;
        this._abortController.abort();
        this._itemSetChangeObserver.disconnect();
        this._skeletonObserver0.disconnect();
        this._skeletonObserver1.disconnect();
        this._visibilityObserver.disconnect();
        this._sizeObserver.disconnect();
        this._whenRequestDataCompleted?.resolve(undefined);
        clearInterval(this._safetyTimerHandle);
        this._ref.removeEventListener('scroll', this.onScroll);
    }

    private get isRendering(): boolean {
        return !!this._renderStartedAt;
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

    private parseRenderState(): VirtualListRenderState | null {
        try {
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
        catch (e) {
            warnLog?.log("parseRenderState(): failed", e);
            return null;
        }
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private onItemSetChange = (mutations: MutationRecord[], _observer: MutationObserver): void => {
        if (!this.isRendering) {
            if (mutations.length > 0)
                warnLog?.log('onItemSetChange: there are mutations, but isRendering() == false')
            this._renderStartedAt = Date.now();
        }
        this._cachedAllItemRefs = null;
        const startedAt = this._renderStartedAt;
        if (debugLog) {
            const removedCount = mutations.reduce((prev, m) => prev + m.removedNodes.length, 0);
            const addedCount = mutations.reduce((prev, m) => prev + m.addedNodes.length, 0);
            const queryDuration = Math.max(0, startedAt - (this._lastQueryTime ?? startedAt));
            debugLog.log(
                `onItemSetChange: query duration: `, queryDuration,
                '; added: ', addedCount,
                '; removed: ', removedCount,
                '; startedAt: ', startedAt);
        }

        // request recalculation of the item range and order item list as we've got new items
        this._shouldRecalculateItemRange = true;
        this._shouldUpdateOrderedItems = true;

        // copy existing items - because we can remove them and add again at another tiles
        const oldItems = new Map<string, VirtualListItem>(this._items);
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
            }
            for (const node of mutation.addedNodes) {
                const itemRef = node as HTMLElement;
                const key = getItemKey(itemRef);
                if (!key)
                    continue;

                const newItem = this.createListItem(key, itemRef);
                const oldItem = oldItems.get(key);
                if (oldItem) {
                    this._items.set(key, oldItem);
                    if (oldItem.size > 0)
                        this._unmeasuredItems.delete(key);
                }
                else
                    this._items.set(key, newItem);
            }
        }
        this.updateOrderedItems();
        void this.endRender();
    };

    private onResize = (entries: ResizeObserverEntry[], _observer: ResizeObserver): void => {
        let itemsWereMeasured = false;
        const itemRefsWithWrongSize = new Array<HTMLElement>();
        for (const entry of entries) {
            const rect = entry.contentRect;
            const key = getItemKey(entry.target as HTMLElement);
            const size = rect.height;

            const item = this._items.get(key);
            if (item) {
                if (item.size < 0 && size == 0) {
                    itemRefsWithWrongSize.push(entry.target as HTMLElement);
                }
                else {
                    const hasRemoved = this._unmeasuredItems.delete(key);
                    itemsWereMeasured ||= hasRemoved;
                    item.size = size;
                }
            }
            else {
                const hasRemoved = this._unmeasuredItems.delete(key);
                itemsWereMeasured ||= hasRemoved;
            }
        }
        if (itemRefsWithWrongSize.length) {
            // ensure we have all sizes calculated
            fastRaf(() => {
                for (const itemRef of itemRefsWithWrongSize) {
                    const key = getItemKey(itemRef);
                    const item = this._items.get(key);
                    if (item.size < 0) {
                        const itemRect = itemRef.getBoundingClientRect();
                        item.size = itemRect.height;
                    }
                    const hasRemoved = this._unmeasuredItems.delete(key);
                    itemsWereMeasured ||= hasRemoved;
                }

                if (this._unmeasuredItems.size == 0)
                    this.updateViewportThrottled();

                // recalculate item range as some elements were updated
                this._shouldRecalculateItemRange = itemsWereMeasured;
            });
        }

        const lastItemWasMeasured = itemsWereMeasured && this._unmeasuredItems.size == 0;
        if (lastItemWasMeasured)
            this.updateViewportThrottled();

        // recalculate item range as some elements were updated
        this._shouldRecalculateItemRange = itemsWereMeasured;
    };

    private onItemVisibilityChange = (entries: IntersectionObserverEntry[], _observer: IntersectionObserver): void => {
        if (this.isRendering)
            return;

        let hasChanged = false;
        const rs = this._renderState;
        const lastItemKey = this.getLastItemKey();
        const firstItemKey = this.getFirstItemKey();
        for (const entry of entries) {
            const itemRef = entry.target as HTMLElement;
            const key = getItemKey(itemRef);
            if (!key) {
                if (this._endAnchorRef === itemRef) {
                    if (entry.isIntersecting) {
                        this.turnOnIsEndAnchorVisibleDebounced();
                        this.turnOffIsEndAnchorVisibleDebounced.reset();
                    }
                    else if (this._isEndAnchorVisible) {
                        this.turnOffIsEndAnchorVisibleDebounced();
                        this.turnOnIsEndAnchorVisibleDebounced.reset();
                    }
                }
                continue;
            }
            if (!entry.isIntersecting) {
                hasChanged ||= this._visibleItems.has(key);
                this._visibleItems.delete(key);
            }
            else if ((entry.intersectionRatio >= 0.4 || entry.intersectionRect.height > MinViewPortSize / 2) && entry.isIntersecting) {
                hasChanged ||= !this._visibleItems.has(key);
                this._visibleItems.add(key);
            }
            else if (key === lastItemKey && entry.isIntersecting && rs.hasVeryLastItem && this._isEndAnchorVisible) {
                // the last item is bigger than viewport, but we see the end anchor - so let's mark it visible
                hasChanged ||= !this._visibleItems.has(key);
                this._visibleItems.add(key);
            }

            this._top = entry.rootBounds.top + VisibilityEpsilon;
        }
        if (hasChanged) {
            let hasStickyEdge = false;
            if (rs.hasVeryLastItem) {
                if (this._visibleItems.has(lastItemKey) || this._isEndAnchorVisible) {
                    this.setStickyEdge({ itemKey: lastItemKey, edge: VirtualListEdge.End });
                    hasStickyEdge = true;
                }
            }
            if (!hasStickyEdge && rs.hasVeryFirstItem) {
                if (this._visibleItems.has(firstItemKey)) {
                    this.setStickyEdge({ itemKey: firstItemKey, edge: VirtualListEdge.Start });
                    hasStickyEdge = true;
                }
            }
            if (!hasStickyEdge && this._stickyEdge !== null) {
                this.setStickyEdge(null);
            }

            this.updateVisibleKeysThrottled();
        }
    };

    private updateVisibleItems(): void {
        const visibleItems = [...this._visibleItems];
        for (const itemKey of visibleItems) {
            const itemRef = this.getItemRef(itemKey);
            if (!itemRef) {
                this._visibleItems.delete(itemKey);
                continue;
            }

            const isItemVisible = this.isItemPartiallyVisible(itemRef);
            if (!isItemVisible)
                this._visibleItems.delete(itemKey);
        }
        if (this._visibleItems.size == 0) {
            const itemRefs = this.getAllItemRefs();
            // find visible items
            const visibilityStartIndex = binarySearch(itemRefs, itemRef => {
                const itemRect = itemRef.getBoundingClientRect();
                const viewRect = this._ref.getBoundingClientRect();
                return itemRect.bottom >= viewRect.top;
            });
            const visibilityEndIndex = binarySearch(itemRefs, itemRef => {
                const itemRect = itemRef.getBoundingClientRect();
                const viewRect = this._ref.getBoundingClientRect();
                return itemRect.top >= viewRect.bottom;
            });
            for (let i = visibilityStartIndex; i < visibilityEndIndex; i++) {
                const itemRef = itemRefs[i];
                const itemKey = getItemKey(itemRef);
                if (itemKey)
                    this._visibleItems.add(itemKey);
            }
        }
    }

    private onSkeletonVisibilityChange = (entries: IntersectionObserverEntry[], _observer: IntersectionObserver): void => {
        let isNearSkeleton = false;
        for (const entry of entries) {
            isNearSkeleton ||= entry.isIntersecting
                && entry.boundingClientRect.height > EdgeEpsilon;
        }
        if (isNearSkeleton) {
            this._isNearSkeleton = isNearSkeleton;
            // reset turn off attempt
            this.turnOffIsNearSkeletonDebounced.reset();
            this.updateViewportThrottled();
        }
        else
            this.turnOffIsNearSkeletonDebounced();
        // debug helper
        // console.warn("skeleton triggered", isNearSkeleton);
    };

    private turnOffIsNearSkeletonDebounced = debounce(() => this.turnOffIsNearSkeleton(), ScrollDebounce);
    private turnOffIsNearSkeleton(): void {
        this._isNearSkeleton = false;
        // debug helper
        // console.warn("skeleton os off");
    }

    private turnOffIsEndAnchorVisibleDebounced = debounce(() => this.turnOffIsEndAnchorVisible(), ScrollDebounce);
    private turnOffIsEndAnchorVisible(): void {
        this._isEndAnchorVisible = false;
        if (this._stickyEdge?.edge === VirtualListEdge.End) {
            this.setStickyEdge(null);
        }

        this.updateVisibleKeysThrottled();
    }

    private turnOnIsEndAnchorVisibleDebounced = debounce(() => this.turnOnIsEndAnchorVisible(), ScrollDebounce);
    private async turnOnIsEndAnchorVisible(): Promise<void> {
        // double-check visibility to prevent issues with scroll-to-the-last-item button
        await fastReadRaf();

        const isEndAnchorRefVisible = this.isItemPartiallyVisible(this._endAnchorRef);
        const isEndSpacerRefVisible = this.isItemPartiallyVisible(this._endSpacerRef)
            && this._endSpacerRef.getBoundingClientRect().height > VisibilityEpsilon;
        const isEndAnchorVisible = isEndAnchorRefVisible && !isEndSpacerRefVisible;
        if (!isEndAnchorVisible) {
            this._isEndAnchorVisible = false;
            return;
        }

        this._isEndAnchorVisible = true;
        if (this._renderState.hasVeryLastItem) {
            const edgeKey = this.getLastItemKey();
            this.setStickyEdge({itemKey: edgeKey, edge: VirtualListEdge.End});
        }
        this.updateVisibleKeysThrottled();
    }

    private async endRender(): Promise<void> {
        if (!this.isRendering) {
            this._whenRequestDataCompleted?.resolve(undefined);
            return;
        }

        const rs = this.parseRenderState();
        if (rs === null) {
            this._renderStartedAt = null;
            this._whenRequestDataCompleted?.resolve(undefined);
            return;
        }

        const startedAt = this._renderStartedAt;
        const now = Date.now();
        debugLog?.log(`endRender, renderIndex = #${rs.renderIndex}, duration = ${now - startedAt}ms, rs =`, rs);
        try {
            this._renderState = rs;

            // fix iOS MAUI scroll issue after first renders
            if (rs.renderIndex === 0 && DeviceInfo.isIos)
                fastRaf({ write: () => this.forceReflow() });

            // Update statistics
            const ratio = this._statistics.responseFulfillmentRatio;
            if (rs.requestedStartExpansion > 0 && !rs.hasVeryFirstItem)
                this._statistics.addResponse(rs.startExpansion, rs.requestedStartExpansion * ratio);
            if (rs.requestedEndExpansion > 0 && !rs.hasVeryLastItem)
                this._statistics.addResponse(rs.endExpansion, rs.requestedEndExpansion * ratio);

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
            else if (this._pivots.length) {
                await this.restoreScrollPosition(startedAt);

                // ensure scroll position and size are recalculated
                await fastWriteRaf();
            }
            else {
                warnLog?.log(`endRender: there are no pivots`);
            }
        } finally {
            this._renderStartedAt = null;
            this._whenRequestDataCompleted?.resolve(undefined);

            this._pivots = [];
            this._itemRange = null;
            this._viewport = null;

            // trigger update only for first render to load data if needed
            if (rs.renderIndex <= 1)
                void this.updateViewport();
        }
    }

    private readonly updateViewportThrottled = throttle(this.updateViewport, UpdateViewportInterval, 'default', 'updateViewport');
    private async updateViewport(): Promise<void> {
        const rs = this._renderState;
        if (this._isDisposed || this.isRendering)
            return;

        // do not update client state when we haven't completed rendering for the first time
        if (rs.renderIndex === -1)
            return;

        await fastReadRaf();

        const prevViewportSize = this._viewport?.size ?? Number.MAX_SAFE_INTEGER;
        const rangeStarts = new Array<number>();
        const rangeEnds = new Array<number>();
        if (this._visibleItems.size == 0) {
            this.updateVisibleItems();
        }
        const visibleItems = this._visibleItems;
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
            else {
                if (this.ensureItemRangeCalculated()) {
                    return await this.updateViewport();
                }
            }
        }

        let viewport: NumberRange | null = (!this.fullRange || rangeStarts.length === 0)
            ? null
            : new NumberRange(Math.min(...rangeStarts), Math.max(...rangeEnds));

        if (viewport && viewport.size > prevViewportSize + MinViewPortSize) {
            // probably we have invalid visible items that provide wrong viewport size
            this.updateVisibleItems();
            viewport = null;
        }

        if (!viewport && this.fullRange) {
            // fallback viewport calculation
            const viewportHeight = this._ref.clientHeight - this._endAnchorRef.getBoundingClientRect().height;
            const scrollHeight = this._ref.scrollHeight;
            const scrollTop = this._ref.scrollTop + scrollHeight - viewportHeight;
            const clientViewport = new NumberRange(scrollTop, scrollTop + viewportHeight);
            const fullRange = this.fullRange;
            if (fullRange != null) {
                viewport = clientViewport.fitInto(fullRange);
            }
        }
        // set min viewport size if smaller
        if (viewport && viewport.size < MinViewPortSize)
            viewport = new NumberRange(viewport.end - MinViewPortSize, viewport.end);

        // update item range
        const isViewportUnknown = viewport == null;
        if (!this.ensureItemRangeCalculated() && !this._itemRange) {
            this.updateViewportThrottled();
        }
        else if (isViewportUnknown)
            await this.updateViewport();
        else {
            if (this._viewport && viewport) {
                if (viewport.start < this._viewport.start)
                    this._scrollDirection = 'up';
                else
                    this._scrollDirection = 'down';
            }

            this._viewport = viewport;
            await this.requestData();
        }
    }

    private readonly updateVisibleKeysThrottled = throttle(() => this.updateVisibleKeys(), UpdateItemVisibilityInterval, 'delayHead', 'updateVisibleKeys');
    private readonly updateVisibleKeys = serialize(async () => {
        if (this._isDisposed)
            return;

        await fastReadRaf();
        this.updateVisibleItems();
        const visibleItems = [...this._visibleItems].sort(this._keySortCollator.compare);
        debugLog?.log(`updateVisibleKeys: calling UpdateItemVisibility:`, visibleItems, this._isEndAnchorVisible);
        await this._blazorRef.invokeMethodAsync('UpdateItemVisibility', this._identity, visibleItems, this._isEndAnchorVisible);
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
        this._shouldUpdateOrderedItems = false;
    }

    private createListItem(itemKey: string, itemRef: HTMLElement): VirtualListItem {
        const countAs = getItemCountAs(itemRef);
        const newItem = new VirtualListItem(itemKey, countAs ?? 1);
        this._unmeasuredItems.add(itemKey);
        this._sizeObserver.observe(itemRef, { box: 'border-box' });
        this._visibilityObserver.observe(itemRef);
        return newItem;
    }

    // Event handlers

    private onSafetyTimer = (): void => {
        const pendingItemSetChanges = this._itemSetChangeObserver.takeRecords();
        if (pendingItemSetChanges.length > 0) {
            warnLog?.log(`onSafetyTimer: pending item set changes found`);
            this.onItemSetChange(pendingItemSetChanges, this._itemSetChangeObserver);
        }
        const pendingVisibilityChanges = this._visibilityObserver.takeRecords();
        if (pendingVisibilityChanges.length > 0) {
            warnLog?.log(`onSafetyTimer: pending visibility changes found`);
            this.onItemVisibilityChange(pendingVisibilityChanges, this._visibilityObserver);
        }
        if (this._isNearSkeleton)
            this.updateViewportThrottled();
    };

    private onScroll = (): void => {
        this._isScrolling = true;
        this.turnOffIsScrollingDebounced();

        // large messages is being displayed and probably can have outdated pivot offset
        // let's update offset
        if (this.isRendering)
            return;

        this.updateViewportThrottled();
    };

    private updateCurrentPivots(): void {
        const time = Date.now();
        const pivots = new Array<Pivot>();
        const pivotRefs = new Array<HTMLElement>();
        // add query edges and first\last items as pivots
        const itemKeys = [this.getFirstItemKey(), this._query.keyRange?.start, this._query.keyRange?.end, this.getLastItemKey()];
        for (let itemKey of itemKeys) {
            if (pivots.some(p => p.itemKey === itemKey))
                continue;

            const pivotRef = this.getItemRef(itemKey);
            if (!pivotRef)
                continue;

            pivotRefs.push(pivotRef);
            // measure scroll position
            const itemRect = pivotRef.getBoundingClientRect();
            const pivot: Pivot = {
                itemKey,
                offset: Math.ceil(itemRect.bottom),
                time,
            };
            pivots.push(pivot);
        }
        if (pivots.length)
            this._pivots = pivots;

        for (const pivotRef of pivotRefs) {
            // set native scroll pivot
            pivotRef.style.overflowAnchor = 'auto';
        }
    }

    private turnOffIsScrollingDebounced = debounce(() => this.turnOffIsScrolling(), ScrollDebounce);
    private turnOffIsScrolling() {
        this._isScrolling = false;
        this._scrollDirection = 'none';

        // this line below can fix rendering artifacts when some entries are blank
        // but adds significant stutter during scroll
        // this.forceRepaintThrottled();

        if (this.isRendering || this._isDisposed)
            return;

        this.updateViewportThrottled();
    }

    private getAllItemRefs(): HTMLLIElement[] {
        if (this._cachedAllItemRefs === null) {
            const elementRefs = this._containerRef.children as HTMLCollectionOf<HTMLLIElement>;
            this._cachedAllItemRefs = Array.from(elementRefs);
        }
        return this._cachedAllItemRefs;
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

    private isItemPartiallyVisible(itemRef: HTMLElement): boolean {
        const itemRect = itemRef.getBoundingClientRect();
        const viewRect = this._ref.getBoundingClientRect();
        return itemRect.bottom > viewRect.top && itemRect.top < viewRect.bottom;
    }

    private forceReflow(): void {
        this._ref.style.display = 'none';
        void this._ref.offsetWidth;
        this._ref.style.display = '';
    }

    private scrollTo(
        itemRef?: HTMLElement,
        useSmoothScroll: boolean = false,
        blockPosition: ScrollLogicalPosition = 'nearest') {
        debugLog?.log(`scrollTo, item key:`, getItemKey(itemRef));
        this._inertialScroll.interrupt();
        this._scrollTime = Date.now();
        if (itemRef) {
            const authorBadge = itemRef.querySelector('div.c-author-badge');
            const navigateTarget = authorBadge ?? itemRef;
            navigateTarget.scrollIntoView({
                behavior: useSmoothScroll ? 'smooth' : 'auto',
                block: blockPosition,
                inline: 'nearest',
            });
        }
    }

    private scrollToEnd(
        useSmoothScroll: boolean = false) {
        debugLog?.log('scrollTo end');
        this.scrollTo(this._endAnchorRef, useSmoothScroll, 'end');
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

    private async restoreScrollPosition(renderTime: number): Promise<void> {
        debugLog?.log(`restoreScrollPosition: pivots`, [...this._pivots], renderTime);

        let pivots = this._pivots;
        pivots.sort((l,r) => Math.abs(l.offset) - Math.abs(r.offset));
        for (const pivot of pivots) {
            // resync scroll to make pivot ref position the same within viewport
            const pivotRef = this.getItemRef(pivot.itemKey);
            if (!pivotRef)
                continue;

            let scrollTop: number | null = null;
            let shouldResync = false;

            // fastRaf performs read\update in proper order during single call to requestAnimationFrame
            fastRaf({
                read: () => {
                    const pivotOffset = pivot.offset;
                    const itemRect = pivotRef.getBoundingClientRect();
                    const currentPivotOffset = itemRect.bottom;
                    const dPivotOffset = pivotOffset - currentPivotOffset;
                    scrollTop = this._ref.scrollTop;
                    if (Math.abs(dPivotOffset) > PivotSyncEpsilon) {
                        debugLog?.log(`restoreScrollPosition: [${pivot.itemKey}]: ~${scrollTop} = ${pivotOffset} ~> ${itemRect.bottom} + ${dPivotOffset}`, pivot);
                        scrollTop -= dPivotOffset;
                        shouldResync = true;
                    }
                },
                write: () => {
                    if (shouldResync) {
                        // debug helper
                        // pivotRef.style.backgroundColor = `rgb(${Math.random() * 255},${Math.random() * 255},${Math.random() * 255})`;

                        // set scroll styles to improve UX on iOS before setting scrollTop
                        this._inertialScroll.freeze();
                        this._ref.style.overscrollBehaviorY = 'none';
                        this._ref.scrollTop = scrollTop;
                        fastRaf({
                            write: () => {
                                // reset scroll styles
                                this._ref.style.overscrollBehaviorY = 'auto';
                                this._inertialScroll.unfreeze();
                            }
                        });
                        debugLog?.log(`restoreScrollPosition: scroll set`, scrollTop);
                    } else if (this._isNearSkeleton && Math.abs(scrollTop) < PivotSyncEpsilon) {
                        debugLog?.log(`restoreScrollPosition: scrollTop ~= 0`, this.isRendering);

                        // we have lost scroll offset so let's scroll to the last visible pivot
                        this.scrollTo(pivotRef, false, 'center');
                    } else
                        debugLog?.log(`restoreScrollPosition: skipped [${pivot.itemKey}]: ~${scrollTop}`, pivot);
                },
            });

            return;
        }
        warnLog?.log(`restoreScrollPosition: there are no pivot refs found!`);
    }

    private async measureItems(): Promise<void> {
        if (!this.hasUnmeasuredItems)
            return;

        await fastReadRaf();
        const unmeasuredItems = [...this._unmeasuredItems];
        let itemsWereMeasured = false;
        for (const key of unmeasuredItems) {
            const item = this._items.get(key);
            if (item.size < 0) {
                const itemRef = this.getItemRef(key);
                const itemRect = itemRef.getBoundingClientRect();
                item.size = itemRect.height;
            }
            const hasRemoved = this._unmeasuredItems.delete(key);
            itemsWereMeasured ||= hasRemoved;
        }

        // recalculate item range as some elements were updated
        this._shouldRecalculateItemRange = itemsWereMeasured;
    }

    private ensureItemRangeCalculated(): boolean {
        // nothing to do when unmeasured items still exist or there were no new renders
        if (this.hasUnmeasuredItems) {
            void this.measureItems();
            return false;
        }

        if (this._shouldUpdateOrderedItems)
            this.updateOrderedItems();

        if (!this._shouldRecalculateItemRange && this._itemRange)
            return false;

        // nothing to do when there are no items rendered
        if (this._orderedItems.length == 0)
            return false;

        const orderedItems = this._orderedItems;
        // find rightmost measured item
        let cornerStoneItemIndex = orderedItems.length - 1;
        let cornerStoneItem = orderedItems[cornerStoneItemIndex];
        while (cornerStoneItemIndex > 0 && !cornerStoneItem.isMeasured)
            cornerStoneItem = orderedItems[--cornerStoneItemIndex];

        if (!cornerStoneItem.range) {
            cornerStoneItemIndex = orderedItems.length - 1;
            cornerStoneItem = orderedItems[cornerStoneItemIndex];
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
            orderedItems[orderedItems.length - 1].range.end);

        this._shouldRecalculateItemRange = false;
        return true;
    }

    private async requestData(): Promise<void> {
        if (this.isRendering || !this._viewport || !this._itemRange)
            return;

        const query = this.getDataQuery();
        if (!this.mustRequestData(query)) {
            debugLog?.log(`requestData: request is unnecessary`);
            return;
        }
        if (query.isNone)
            return;

        this._query = query;

        const whenRequestDataCompleted = this._whenRequestDataCompleted;
        if (whenRequestDataCompleted && !whenRequestDataCompleted.isCompleted()) {
            debugLog?.log(`requestData: the previous request is not completed yet`);
            return;
        }

        const newWhenRequestDataCompleted = new PromiseSourceWithTimeout<void>();
        newWhenRequestDataCompleted.setTimeout(RequestDataTimeout, () => {
            newWhenRequestDataCompleted.resolve(undefined);
        });
        this._whenRequestDataCompleted = newWhenRequestDataCompleted;

        await fastReadRaf();
        this.updateCurrentPivots();

        debugLog?.log(`requestData: query:`, this._query, [...this._pivots], this._viewport, this._viewport?.size);
        this._lastQueryTime = Date.now();
        // debug helper
        // await delayAsync(150);
        await this._blazorRef.invokeMethodAsync('RequestData', this._query);
        this._lastQuery = this._query;
    }

    private mustRequestData(query: VirtualListDataQuery): boolean {
        const itemRange = this._itemRange;
        const queryRange = query.virtualRange;
        const viewport = this._viewport;
        if (!itemRange || !queryRange)
            return false;

        if (!viewport)
            return false;

        if (this._query === query)
            return false;

        if (itemRange.contains(query.virtualRange))
            return false;

        if (query.expandStartBy == 0 && query.expandEndBy == 0)
            return false;

        const viewportSize = viewport.size;
        const commonRange = itemRange.intersectWith(queryRange);
        if (commonRange.isEmpty)
            return true;

        const mustExpand =
            Math.abs(commonRange.start - queryRange.start) > viewportSize // we are loading more than a viewport at the start edge
            || Math.abs(queryRange.end - commonRange.end) > viewportSize // we are loading more than a viewport at the end edge
            || (!this._renderState.hasVeryFirstItem && Math.abs(viewport.start - itemRange.start) > viewportSize) // viewport is close to the start edge and there are items above
            || (!this._renderState.hasVeryLastItem && Math.abs(itemRange.end - viewport.end) > viewportSize) // viewport is close to the end edge and there are items bellow
            || this._isNearSkeleton && (commonRange.start > queryRange.start || queryRange.end > commonRange.end); // the viewport is near skeletons and we are loading some more
        // NOTE(AY): The condition below checks just one side
        const mustContract = Math.abs(itemRange.end - commonRange.end) > viewportSize * 2;
        return mustExpand || mustContract;
    }

    private getDataQuery(): VirtualListDataQuery {
        const rs = this._renderState;
        const itemSize = this._statistics.itemSize;
        const responseFulfillmentRatio = this._statistics.responseFulfillmentRatio;
        const viewport = this._viewport;
        const alreadyLoaded = this._itemRange;
        if (!viewport || !alreadyLoaded)
            return this._lastQuery;

        if (this.hasUnmeasuredItems) { // Let's wait for measurement to complete first
            void this.measureItems();
            return this._lastQuery;
        }

        const viewportSize = viewport.size;
        const lastQuerySide = this._lastQuery.expandStartBy == 0 && this._lastQuery.expandEndBy == 0
            ? 'none'
            : this._lastQuery.expandStartBy == 0
                ? 'end'
                : 'start';
        const alreadyLoadedFromStart = Math.abs(alreadyLoaded.start - viewport.start);
        const alreadyLoadedTillEnd = Math.abs(alreadyLoaded.end - viewport.end);
        const loadZoneTrigger = viewportSize * 2;
        const loadZoneSize = loadZoneTrigger * 2;
        let loadStart = viewport.start - viewportSize * 1.5; // keep at least 1.5 viewport more in both directions
        let loadEnd = viewport.end + viewportSize * 1.5;

        switch (lastQuerySide) {
            case "none":
                // check whether we need to load from the start first
                if (alreadyLoadedFromStart < loadZoneTrigger) {
                    if (!rs.hasVeryFirstItem)
                        loadStart = viewport.start - loadZoneSize; // extend from the start
                }
                else if (alreadyLoadedTillEnd < loadZoneTrigger) {
                    if (!rs.hasVeryLastItem)
                        loadEnd = viewport.end + loadZoneSize; // extend from the end
                }
                break;
            case "end":
                // check whether we need to continue loading from the end
                if (alreadyLoadedTillEnd < loadZoneTrigger) {
                    if (!rs.hasVeryLastItem)
                        loadEnd = viewport.end + loadZoneSize;
                }
                else if (alreadyLoadedFromStart < viewportSize / 3) { // smaller than half of viewport to change load direction
                    debugLog?.log('getDataQuery: previous direction was _end_', viewport, alreadyLoaded, loadZoneSize);
                    if (!rs.hasVeryFirstItem)
                        loadStart = viewport.start - loadZoneSize;
                    else
                        return this._lastQuery;
                }
                break;
            case "start":
                // check whether we need to continue loading from the start
                if (alreadyLoadedFromStart < loadZoneTrigger) {
                    if (!rs.hasVeryFirstItem)
                        loadStart = viewport.start - loadZoneSize;
                }
                else if (alreadyLoadedTillEnd < viewportSize / 3) { // smaller than 1/3 of viewport to change load direction
                    debugLog?.log('getDataQuery: previous direction was _start_', viewport, alreadyLoaded, loadZoneSize);
                    if (!rs.hasVeryLastItem)
                        loadEnd = viewport.end + loadZoneSize;
                    else
                        return this._lastQuery;
                }
                break;
        }

        // adjust to existing data range
        if (loadStart < alreadyLoaded.start && rs.hasVeryFirstItem)
            loadStart = alreadyLoaded.start;
        if (loadEnd > alreadyLoaded.end && rs.hasVeryLastItem)
            loadEnd = alreadyLoaded.end;
        const loadZone = new NumberRange(loadStart, loadEnd);

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
            if(!item.isChatEntry)
                continue;

            if (item.range.size == 0)
                continue; // skip items with zero height

            if (item.isMeasured && item.range.intersectWith(loadZone).size > 0) {
                endIndex = i;
                if (startIndex < 0)
                    startIndex = i;
            } else if (startIndex >= 0)
                break;
        }

        const firstItem = items[startIndex];
        const lastItem = items[endIndex];
        const startGap = Math.max(0, firstItem.range.start - loadZone.start);
        const endGap = Math.max(0, loadZone.end - lastItem.range.end);
        // skip queries that load few more items - we prefer to load more
        if (startGap < viewportSize && endGap < viewportSize)
            return this._lastQuery;

        const expandStartBy = clamp(Math.ceil(startGap / itemSize / responseFulfillmentRatio), 0, MaxExpandBy);
        const expandEndBy = clamp(Math.ceil(endGap / itemSize / responseFulfillmentRatio), 0, MaxExpandBy);
        const keyRange = new Range(firstItem.key, lastItem.key);
        const query = new VirtualListDataQuery(keyRange, loadZone);
        query.expandStartBy = expandStartBy;
        query.expandEndBy = expandEndBy;
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

/**
 * Return 0 <= i <= array.length such that !pred(array[i - 1]) && pred(array[i]).
 */
function binarySearch<T>(array: T[], pred: (item: T) => boolean): number {
    let low = -1;
    let high = array.length;
    while (1 + low < high) {
        const mid = low + ((high - low) >> 1);
        if (pred(array[mid])) {
            high = mid;
        } else {
            low = mid;
        }
    }
    if (high == array.length)
        return -1;

    return high;
}

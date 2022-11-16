import './virtual-list.css';
import { delayAsync, throttle, serialize, PromiseSource } from 'promises';
import { VirtualListClientSideItem, VirtualListClientSideState } from './ts/virtual-list-client-side-state';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListStickyEdgeState } from './ts/virtual-list-sticky-edge-state';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListRenderPlan } from './ts/virtual-list-render-plan';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { Range } from './ts/range';
import { VirtualListStatistics } from './ts/virtual-list-statistics';
import { VirtualListAccessor } from './ts/virtual-list-accessor';
import { clamp } from './ts/math';
import { RangeExt } from './ts/range-ext';
import { Pivot } from './ts/pivot';

import { Log, LogLevel } from 'logging';

const LogScope = 'VirtualList';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const UpdateClientSideStateInterval: number = 125;
const UpdateVisibleKeysInterval: number = 250;
const IronPantsHandlePeriod: number = 1600;
const PivotSyncEpsilon: number = 16;
const VisibilityEpsilon: number = 5;
const EdgeEpsilon: number = 4;
const MaxExpandBy: number = 320;
const RenderTimeout: number = 640;
const UpdateTimeout: number = 1200;
const DefaultLoadZone: number = 2000;

export class VirtualList implements VirtualListAccessor {
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
    private readonly _skeletonObserver: IntersectionObserver;
    private readonly _ironPantsIntervalHandle: number;
    private readonly _unmeasuredItems: Set<string>;
    private readonly _visibleItems: Set<string>;

    private _isDisposed = false;
    private _stickyEdge: Required<VirtualListStickyEdgeState> | null = null;
    private _whenRenderCompleted: PromiseSource<void> | null = null;
    private _whenUpdateCompleted: PromiseSource<void> | null = null;
    private _pivots: Pivot[] = [];
    private _top: number;

    private _isRendering: boolean = false;
    private _isNearSkeleton: boolean = false;
    private _scrollTime: number | null = null;

    private _lastPlan?: VirtualListRenderPlan = null;
    private _plan: VirtualListRenderPlan;
    private _lastQuery: VirtualListDataQuery = VirtualListDataQuery.None;
    private _lastQueryTime: number | null = null;
    private _query: VirtualListDataQuery = VirtualListDataQuery.None;

    public renderState: VirtualListRenderState;
    public clientSideState: VirtualListClientSideState;
    public readonly statistics: VirtualListStatistics = new VirtualListStatistics();
    public readonly items: Record<string, VirtualListClientSideItem>;

    public get loadZoneSize() {
        return (this.clientSideState?.viewportHeight ?? DefaultLoadZone) * 2;
    }

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject
    ) {
        return new VirtualList(ref, backendRef);
    }

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject
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
        const visibilityThresholds = [...Array(100).keys() ].map(i => i / 100);
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
                delay: 250  // minimum 100
            });
        this._scrollPivotObserver = new IntersectionObserver(
            this.onScrollPivotVisibilityChange,
            {
                root: this._ref,
                // Track pivot positions near center of the virtual list viewport.
                rootMargin: '-30%',
                // Receive callback on any intersection change, even 1 percent.
                threshold: visibilityThresholds,
            });
        this._skeletonObserver = new IntersectionObserver(
            this.onSkeletonVisibilityChange,
            {
                root: this._ref,
                // Extend visibility outside of the viewport by 1/4 of the loadzone
                rootMargin: `${Math.round(this.loadZoneSize/4)}px`,
                threshold: visibilityThresholds,
            });

        this._ironPantsIntervalHandle = self.setInterval(this.onIronPantsHandle, IronPantsHandlePeriod);

        this._unmeasuredItems = new Set<string>();
        this._visibleItems = new Set<string>();

        this._skeletonObserver.observe(this._spacerRef);
        this._skeletonObserver.observe(this._endSpacerRef);

        this.items = {};
        this.renderState = {
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
        this.clientSideState = {
            renderIndex: 0,

            scrollTop: 0,
            viewportHeight: 0,
            stickyEdge: null,

            visibleKeys: [],
        };

        this._plan = new VirtualListRenderPlan(this);
        this.maybeOnRenderEnd([], this._renderEndObserver);
    };

    public dispose() {
        this._isDisposed = true;
        this._abortController.abort();
        this._renderEndObserver.disconnect();
        this._skeletonObserver.disconnect();
        this._visibilityObserver.disconnect();
        this._scrollPivotObserver.disconnect();
        this._sizeObserver.disconnect();
        this._whenRenderCompleted?.resolve(undefined);
        this._whenUpdateCompleted?.resolve(undefined);
        clearInterval(this._ironPantsIntervalHandle);
        this._ref.removeEventListener('scroll', this.onScroll);
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private maybeOnRenderEnd = (mutations: MutationRecord[], observer: MutationObserver): void => {
        this._isRendering = true;
        debugLog?.log(`maybeOnRenderEnd: `, mutations.length);

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
                    delete this.items[key];
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

                if (this.items.hasOwnProperty(key)) {
                    itemRef.classList.remove('new');
                    continue;
                }

                this.items[key] = {
                    size: -1,
                    countAs: countAs ?? 1,
                }
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

                if (this.items.hasOwnProperty(key)) {
                    continue;
                }

                this.items[key] = {
                    size: -1,
                    countAs: countAs ?? 1,
                }
                this._unmeasuredItems.add(key);
                this._sizeObserver.observe(itemRef, { box: 'border-box' });
                this._visibilityObserver.observe(itemRef);
                this._scrollPivotObserver.observe(itemRef);
            }
        }

        requestAnimationFrame(time => {
            // make rendered items visible
            for (const itemRef of this.getNewItemRefs()) {
                itemRef.classList.remove('new');
            }

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
        for (const entry of entries) {
            const contentBoxSize = Array.isArray(entry.contentBoxSize)
                ? entry.contentBoxSize[0]
                : entry.contentBoxSize;

            const key = getItemKey(entry.target as HTMLElement);
            this._unmeasuredItems.delete(key);

            const item = this.items[key];
            if (item) {
                item.size = contentBoxSize.blockSize;
            }
        }
    }

    private onItemVisibilityChange = (entries: IntersectionObserverEntry[], observer: IntersectionObserver): void => {
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
            const rs = this.renderState;
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
                        history.pushState("", document.title, location.pathname + location.search);
                    }
                }
            }

            this.updateVisibleKeysThrottled();
        }
    }

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
    }

    private onSkeletonVisibilityChange = (entries: IntersectionObserverEntry[], observer: IntersectionObserver): void => {
        let isNearSkeleton = false;
        for (const entry of entries) {
            isNearSkeleton ||= entry.isIntersecting
                && entry.boundingClientRect.height > EdgeEpsilon
                && !this._isRendering;
        }
        this._isNearSkeleton = isNearSkeleton;
    }

    private getRenderState(): VirtualListRenderState | null {
        const rsJson = this._renderStateRef.textContent;
        if (rsJson == null || rsJson === '')
            return null;

        const rs = JSON.parse(rsJson) as Required<VirtualListRenderState>;
        if (rs.renderIndex <= this.renderState.renderIndex)
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
            this.renderState = rs;

            // Update statistics
            const ratio = this.statistics.responseFulfillmentRatio;
            if (rs.query.expandStartBy > 0 && !rs.hasVeryFirstItem)
                this.statistics.addResponse(rs.startExpansion, rs.query.expandStartBy * ratio);
            if (rs.query.expandEndBy > 0 && !rs.hasVeryLastItem)
                this.statistics.addResponse(rs.endExpansion, rs.query.expandEndBy * ratio);

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
            this._whenRenderCompleted?.resolve(undefined)
            this._whenUpdateCompleted?.resolve(undefined)

            // trigger update only for first render to load data if needed
            if (rs.renderIndex <= 1)
                void this.updateClientSideState();
        }
    }

    private updateClientSideStateThrottled = throttle(() => this.updateClientSideState(), UpdateClientSideStateInterval, 'delayHead');
    private updateClientSideState = serialize(async () => {
        const rs = this.renderState;
        if (this._isDisposed || this._isRendering)
            return;

        // Do not update client state when we haven't completed rendering for the first time
        if (rs.renderIndex === -1)
            return;

        const whenRenderCompleted = this._whenRenderCompleted;
        if (whenRenderCompleted)
            await Promise.race([whenRenderCompleted, delayAsync(RenderTimeout)]);
        const whenUpdateCompleted = this._whenUpdateCompleted;
        if (whenUpdateCompleted)
            await Promise.race([whenUpdateCompleted, delayAsync(UpdateTimeout)]);

        this._lastPlan = this._plan;
        const state = await new Promise<VirtualListClientSideState>(resolve => {
            let state: VirtualListClientSideState = null;
            requestAnimationFrame(time => {
                try {
                    const viewportHeight = this._ref.clientHeight;
                    const scrollTop = this.getVirtualScrollTop();
                    state = {
                        renderIndex: rs.renderIndex,

                        scrollTop: scrollTop,
                        viewportHeight: viewportHeight,
                        stickyEdge: this._stickyEdge,

                        visibleKeys: [],
                    } as VirtualListClientSideState;

                    state.visibleKeys = [...this._visibleItems].sort();
                } finally {
                    resolve(state);
                }
            });
        });

        debugLog?.log(`updateClientSideState: state:`, state);

        const expectedRenderIndex = this.renderState.renderIndex;
        if (state.renderIndex != expectedRenderIndex)
            return;

        this.clientSideState = state;
        const plan = this._plan = this._lastPlan.next();

        if (!plan.isFullyLoaded)
            await this.requestData();
    }, 2);

    private updateVisibleKeysThrottled = throttle(() => this.updateVisibleKeys(), UpdateVisibleKeysInterval, 'delayHead');
    private updateVisibleKeys = serialize(async () => {
        if (this._isDisposed)
            return;

        const visibleKeys = [...this._visibleItems].sort();
        debugLog?.log(`updateVisibleKeys: calling UpdateVisibleKeys:`, visibleKeys);

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
            void this.updateClientSideState();
        }
    }

    private onScroll = (): void => {
        if (this._isRendering || this._isDisposed) {
            return;
        }
        this.updateClientSideStateThrottled();
    };

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

    private getVirtualScrollTop(): number {
        const scrollHeight = this._ref.scrollHeight;
        const spacerHeight = this._spacerRef.clientHeight;
        let scrollTop = this._ref.scrollTop;
        const viewportHeight = this._ref.clientHeight;
        scrollTop += scrollHeight - viewportHeight;
        scrollTop -= spacerHeight;
        return scrollTop;
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

    private async requestData(): Promise<void> {
        if (this._plan.isFullyLoaded || this._isRendering)
            return;

        this._query = this.getDataQuery();
        if (this._query.isSimilarTo(this._lastQuery) && !this._isNearSkeleton)
            return;
        if(this._query.isNone)
            return;

        debugLog?.log(`requestData: query:`, this._query);

        this._whenUpdateCompleted = new PromiseSource<void>();

        // debug helper
        // await delayAsync(50);
        await this._blazorRef.invokeMethodAsync('RequestData', this._query);
        this._lastQuery = this._query;
        this._lastQueryTime = Date.now();
    }

    private getDataQuery(): VirtualListDataQuery {
        // data chunk has already been requested in less a second
        // probably we are scrolling intensively
        const needsMoreAndMore = Date.now() - this._lastQueryTime < 1000;
        const plan = this._plan;
        const rs = this.renderState;
        const itemSize = this.statistics.itemSize;
        const responseFulfillmentRatio = this.statistics.responseFulfillmentRatio;
        const viewport = plan.viewport;
        if (!viewport) {
            return this._lastQuery;
        }
        const alreadyLoaded = plan.itemRange;
        const loadZoneSize = this.loadZoneSize;
        let loadStart = viewport.Start - loadZoneSize;
        if (loadStart < alreadyLoaded.Start && rs.hasVeryFirstItem)
            loadStart = alreadyLoaded.Start;
        let loadEnd = viewport.End + loadZoneSize;
        if (loadEnd > alreadyLoaded.End && rs.hasVeryLastItem)
            loadEnd = alreadyLoaded.End;
        const isScrollingUp = alreadyLoaded.Start - loadStart > loadEnd - alreadyLoaded.End;
        let bufferZoneSize = loadZoneSize * 3;
        if (needsMoreAndMore) {
            const loadMore = loadZoneSize * 2;
            bufferZoneSize *= 4;
            if (isScrollingUp) {
                // try to load more in the direction of scrolling
                loadStart -= loadMore;
                debugLog?.log(`getDataQuery: extend load zone start`, loadMore);
            }
            else {
                // try to load more in the direction of scrolling
                loadEnd += loadMore;
                debugLog?.log(`getDataQuery: extend load zone end`, loadMore);
            }
        }
        const loadZone = new Range(loadStart, loadEnd);
        const bufferZone = new Range(
            Math.max(viewport.Start - bufferZoneSize, 0),
            viewport.End + bufferZoneSize);

        if (plan.hasUnmeasuredItems) // Let's wait for measurement to complete first
            return this._lastQuery;
        if (plan.items.length == 0) // No entries -> nothing to "align" the query to
            return this._lastQuery;
        if (RangeExt.contains(alreadyLoaded, loadZone))
            return this._lastQuery;

        let startIndex = -1;
        let endIndex = -1;
        const items = plan.items;
        for (let i = 0; i < items.length; i++) {
            const item = items[i];
            if (item.isMeasured && RangeExt.size(RangeExt.intersectsWith(item.range, bufferZone)) > 0) {
                endIndex = i;
                if (startIndex < 0)
                    startIndex = i;
            } else if (startIndex >= 0) {
                break;
            }
        }
        if (startIndex < 0) {
            // No items inside the bufferZone, so we'll take the first or the last item
            startIndex = endIndex = items[0].range.End < bufferZone.Start ? 0 : items.length - 1;
        }

        const firstItem = items[startIndex];
        const lastItem = items[endIndex];
        const startGap = Math.max(0, firstItem.range.Start - loadZone.Start);
        const endGap = Math.max(0, loadZone.End - lastItem.range.End);
        const expandStartBy = this.renderState.hasVeryFirstItem || startGap === 0
            ? 0
            : clamp(Math.ceil(Math.max(startGap, loadZoneSize) / itemSize), 0, MaxExpandBy);
        const expandEndBy = this.renderState.hasVeryLastItem || endGap === 0
            ? 0
            : clamp(Math.ceil(Math.max(endGap, loadZoneSize) / itemSize), 0, MaxExpandBy);
        const keyRange = new Range(firstItem.Key, lastItem.Key);
        const query = new VirtualListDataQuery(keyRange);
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
        return null;

    return parseInt(countString);
}

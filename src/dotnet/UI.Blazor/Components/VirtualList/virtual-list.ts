import { debounce, Debounced } from 'debounce';
import { throttle, Throttled } from 'throttle';
import { onceAtATime } from 'serialize';
import './virtual-list.css';
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
import { delayAsync } from 'promises';
import { whenCompleted, WhenCompleted } from 'when';

const LogScope: string = 'VirtualList';
const UpdateClientSideStateTimeout: number = 120;
const UpdateVisibleKeysTimeout: number = 320;
const IronPantsHandleTimeout: number = 1600;
const SizeEpsilon: number = 1;
const EdgeEpsilon: number = 4;
const MaxExpandBy: number = 256;
const RenderTimeout: number = 640;
const UpdateTimeout: number = 1200;

export class VirtualList implements VirtualListAccessor {
    private readonly _debugMode: boolean = false;
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
    private readonly _skeletonObserver: IntersectionObserver;
    private readonly _ironPantsHandlerInterval: number;
    private readonly _bufferZoneSize;
    private readonly _unmeasuredItems: Set<string>;
    private readonly _visibleItems: Set<string>;

    private readonly updateClientSideStateThrottled: Throttled<typeof this.updateClientSideState>;
    private readonly updateClientSideStateOnce: typeof this.updateClientSideState;
    private readonly updateVisibleKeysDebounced: Debounced<typeof this.updateVisibleKeys>;

    private _isDisposed = false;
    private _stickyEdge: Required<VirtualListStickyEdgeState> | null = null;
    private _whenRenderCompleted: WhenCompleted | null = null;
    private _whenUpdateCompleted: WhenCompleted | null = null;
    private _lastVisibleKey: string | null = null;
    private _pivotKey: string | null = null;
    private _pivotOffset: number | null = null;
    private _top: number | null = null;
    private _viewport: number | null = null;

    private _isUpdatingClientState: boolean = false;
    private _isRendering: boolean = false;
    private _isNearSkeleton: boolean = false;
    private _scrollTime: number | null = null;

    private _lastPlan?: VirtualListRenderPlan = null;
    private _plan: VirtualListRenderPlan;
    private _lastQuery: VirtualListDataQuery = VirtualListDataQuery.None;
    private _query: VirtualListDataQuery = VirtualListDataQuery.None;

    public renderState: VirtualListRenderState;
    public clientSideState: VirtualListClientSideState;
    public readonly statistics: VirtualListStatistics = new VirtualListStatistics();
    public readonly loadZoneSize;
    public readonly items: Record<string, VirtualListClientSideItem>;

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        loadZoneSize: number,
        bufferZoneSize: number,
        debugMode: boolean) {
        if (debugMode) {
            console.log(`${LogScope}.ctor`);
            window['virtualList'] = this;
        }

        this._debugMode = debugMode;
        this.loadZoneSize = loadZoneSize;
        this._bufferZoneSize = bufferZoneSize;
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
        this._visibilityObserver = new IntersectionObserver(
            this.onItemVisibilityChange,
            {
                root: this._ref,
                threshold: [0, 0.1, 0.9, 1],

                /* required options for IntersectionObserver v2*/
                // @ts-ignore
                trackVisibility: true,
                delay: 100  // minimum 100
            });
        this._skeletonObserver = new IntersectionObserver(
            this.onSkeletonVisibilityChange,
            {
                root: this._ref,
                rootMargin: `${Math.round(loadZoneSize/4)}px`,
                threshold: [0, 0.1, 0.9, 1],
            });

        // @ts-ignore
        this._ironPantsHandlerInterval = setInterval(this.onIronPantsHandle, IronPantsHandleTimeout);

        this._unmeasuredItems = new Set<string>();
        this._visibleItems = new Set<string>();
        this._lastVisibleKey = null;
        this.updateClientSideStateOnce = onceAtATime(this.updateClientSideState);
        this.updateClientSideStateThrottled = throttle(this.updateClientSideStateOnce, UpdateClientSideStateTimeout);
        this.updateVisibleKeysDebounced = debounce(this.updateVisibleKeys, UpdateVisibleKeysTimeout);

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

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        loadZoneSize: number,
        bufferZoneSize: number,
        debugMode: boolean) {
        return new VirtualList(ref, backendRef, loadZoneSize, bufferZoneSize, debugMode);
    }

    public dispose() {
        this._isDisposed = true;
        this._abortController.abort();
        this._renderEndObserver.disconnect();
        this._skeletonObserver.disconnect();
        this._visibilityObserver.disconnect();
        this._sizeObserver.disconnect();
        this._whenRenderCompleted?.complete();
        this._whenUpdateCompleted?.complete();
        clearInterval(this._ironPantsHandlerInterval);
        this._ref.removeEventListener('scroll', this.onScroll);
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private maybeOnRenderEnd = (mutations: MutationRecord[], observer: MutationObserver): void => {
        this._isRendering = true;

        if (this._debugMode)
            console.log(`${LogScope}.maybeOnRenderEnd: `, mutations.length);

        this._whenRenderCompleted?.complete();
        this._whenRenderCompleted = whenCompleted();

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
        let lastKey: string = null;
        for (const entry of entries) {
            const itemRef = entry.target as HTMLElement;
            const key = getItemKey(itemRef);
            if (entry.intersectionRatio <= 0.2 && !entry.isIntersecting) {
                hasChanged ||= this._visibleItems.has(key);
                this._visibleItems.delete(key);
                lastKey = key;
            }
            else if (entry.intersectionRatio >= 0.4 && entry.isIntersecting) {
                hasChanged ||= !this._visibleItems.has(key);
                this._visibleItems.add(key);
            }
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

            if (lastKey) {
                this._lastVisibleKey = lastKey;
            }

            if (!this._isRendering) {
                // some visible item
                const pivotEntity = entries.filter(e => e.isIntersecting ?? e.intersectionRatio == 1).shift();
                if (pivotEntity) {
                    this._pivotKey = getItemKey(pivotEntity.target as HTMLElement);
                    this._pivotOffset = pivotEntity.boundingClientRect.top;
                    this._top = pivotEntity.rootBounds.top;
                    this._viewport = pivotEntity.rootBounds.height;
                } else if (this._lastVisibleKey) {
                    const lastVisibleKey = this._lastVisibleKey;
                    const lastVisibleItemRef = this.getItemRef(lastVisibleKey);
                    if (lastVisibleItemRef) {
                        requestAnimationFrame(time => {
                            const lastItemRef = this.getItemRef(lastKey);
                            this._pivotKey = lastKey;
                            this._pivotOffset = lastItemRef.getBoundingClientRect().top;
                            this._top = this._ref.getBoundingClientRect().top;
                            this._viewport = this._ref.clientHeight;
                        });
                    }
                } else {
                    this._pivotKey = null;
                    this._pivotOffset = null;
                    this._top = null;
                    this._viewport = null;
                }

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

            this.updateVisibleKeysDebounced();
        }
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
        if (this._debugMode)
            console.log(`${LogScope}.onRenderEnd, renderIndex = #${rs.renderIndex}, rs =`, rs);

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
            else if (this._pivotKey != null) {
                // resync scroll to make pivot ref position the same within viewport
                const pivotRef = this.getItemRef(this._pivotKey);
                if (pivotRef) {
                    const pivotOffset = this._pivotOffset;
                    const pivotOffsetScrollDiff = this._top;
                    const newScrollTop = pivotRef.offsetTop - pivotOffset + pivotOffsetScrollDiff;
                    const scrollTop = this._ref.scrollTop;
                    const dScrollTop = newScrollTop - scrollTop;
                    if (Math.abs(dScrollTop) > SizeEpsilon) {
                        if (this._debugMode)
                            console.warn(`${LogScope}.onRenderEnd: resync [${this._pivotKey}]: ${pivotOffset} = ${scrollTop} + ${dScrollTop} -> ${newScrollTop}`);
                        this._ref.scrollTop = newScrollTop;
                    } else {
                        if (this._debugMode) {
                            const itemRect = pivotRef.getBoundingClientRect();
                            console.warn(`${LogScope}.onRenderEnd: resync skipped [${this._pivotKey}]: ${pivotOffset} ~ ${itemRect.top}`);
                        }
                    }
                }
            }

            // wait for render
            await new Promise<void>(resolve => {
                requestAnimationFrame(time => {
                    // skeleton time to time become visible after render and scroll
                    this._isNearSkeleton =
                        this.isItemVisible(this._spacerRef)
                        || this.isItemVisible(this._endSpacerRef);

                    resolve();
                });
            });
        } finally {
            this._isRendering = false;
            this._whenRenderCompleted?.complete();
            this._whenUpdateCompleted?.complete();

            // trigger update only for first render to load data if needed
            if (rs.renderIndex <= 1) {
                void this.updateClientSideStateOnce();
            }
        }
    }

    private async updateClientSideState(): Promise<void> {
        const rs = this.renderState;
        if (this._isDisposed || this._isUpdatingClientState || this._isRendering)
            return;

        // Do not update client state when we haven't completed rendering for the first time
        if (rs.renderIndex === -1)
            return;

        const whenRenderCompleted = this._whenRenderCompleted;
        if (whenRenderCompleted) {
            await Promise.race([whenRenderCompleted, delayAsync(RenderTimeout)]);
        }
        const whenUpdateCompleted = this._whenUpdateCompleted;
        if (whenUpdateCompleted) {
            await Promise.race([whenUpdateCompleted, delayAsync(UpdateTimeout)]);
        }

        this._lastPlan = this._plan;
        try {
            this._isUpdatingClientState = true;
            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideState: #${rs.renderIndex}`);

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

            if (state) {
                if (this._debugMode)
                    console.log(`${LogScope}.updateClientSideState: state:`, state);
                const expectedRenderIndex = this.renderState.renderIndex;
                if (state.renderIndex != expectedRenderIndex) {
                    return;
                }

                this.clientSideState = state;


                const plan = this._plan = this._lastPlan.next();
                if (!plan.isFullyLoaded) {
                    await this.requestData();
                }
            }
        } finally {
            this._isUpdatingClientState = false;
        }
    }

    private async updateVisibleKeys(): Promise<void> {
        if (this._isDisposed)
            return;

        const visibleKeys = [...this._visibleItems].sort();
        if (this._debugMode)
            console.log(
                `${LogScope}.updateVisibleKeys: server call UpdateVisibleKeys:`,
                visibleKeys);

        await this._blazorRef.invokeMethodAsync('UpdateVisibleKeys', visibleKeys);
    }

    // Event handlers

    private onIronPantsHandle = (): void => {
        // check if mutationObserver is stuck
        const mutations = this._renderEndObserver.takeRecords();
        if (mutations.length > 0) {
            if (this._debugMode)
                console.warn(`${LogScope}: Iron pants rock!`);
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
            void this.updateClientSideStateOnce();
        }
    }

    private onScroll = (): void => {
        if (this._isRendering || this._isDisposed)
            return;

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

    private isItemVisible(itemRef: HTMLElement): boolean {
        const itemRect = itemRef.getBoundingClientRect();
        const viewRect = this._ref.getBoundingClientRect();
        return itemRect.bottom > viewRect.top && itemRect.top < viewRect.bottom && itemRect.height > 0;
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
        if (this._debugMode)
            console.warn(`${LogScope}.scrollTo, item key =`, getItemKey(itemRef));
        this._scrollTime = Date.now();
        itemRef?.scrollIntoView(
            {
                behavior: useSmoothScroll ? 'smooth' : 'auto',
                block: blockPosition,
                inline: 'nearest',
            });
    }

    private setStickyEdge(stickyEdge: VirtualListStickyEdgeState): boolean {
        const old = this._stickyEdge;
        if (old?.itemKey !== stickyEdge?.itemKey || old?.edge !== stickyEdge?.edge) {
            if (this._debugMode)
                console.warn(`${LogScope}.setStickyEdge:`, stickyEdge);
            this._stickyEdge = stickyEdge;
            return true;
        }
        return false;
    }

    private async requestData(): Promise<void> {
        if (this._plan.isFullyLoaded)
            return;

        this._query = this.getDataQuery();
        if (this._query.isSimilarTo(this._lastQuery) && !this._isNearSkeleton)
            return;
        if(this._query.isNone)
            return;

        if (this._debugMode)
            console.warn(`${LogScope}.requestData: query:`, this._query);

        this._whenUpdateCompleted = whenCompleted();

        await this._blazorRef.invokeMethodAsync('RequestData', this._query);
        this._lastQuery = this._query;
    }

    private getDataQuery(): VirtualListDataQuery {
        const plan = this._plan;
        const rs = this.renderState;
        const itemSize = this.statistics.itemSize;
        const responseFulfillmentRatio = this.statistics.responseFulfillmentRatio;
        const viewport = plan.viewport;
        if (!viewport) {
            return this._lastQuery;
        }
        const viewportSize = RangeExt.size(viewport);
        const alreadyLoaded = plan.itemRange;
        let loadStart = viewport.Start - this.loadZoneSize;
        if (loadStart < alreadyLoaded.Start && rs.hasVeryFirstItem)
            loadStart = alreadyLoaded.Start;
        let loadEnd = viewport.End + this.loadZoneSize;
        if (loadEnd > alreadyLoaded.End && rs.hasVeryLastItem)
            loadEnd = alreadyLoaded.End;
        const loadZone = new Range(loadStart, loadEnd);
        const bufferZone = new Range(
            Math.max(viewport.Start - this._bufferZoneSize, 0),
            viewport.End + this._bufferZoneSize);

        if (plan.hasUnmeasuredItems) // Let's wait for measurement to complete first
            return this._lastQuery;
        if (plan.items.length == 0) // No entries -> nothing to "align" the query to
            return this._lastQuery;
        if (RangeExt.contains(alreadyLoaded, loadZone))
            return this._lastQuery;
        if (loadZone.Start < alreadyLoaded.Start && (viewport.Start - alreadyLoaded.Start > viewportSize * 2) && !this._isNearSkeleton)
            return this._lastQuery;
        if (loadZone.End > alreadyLoaded.End && (alreadyLoaded.End - viewport.End > viewportSize * 2) && !this._isNearSkeleton)
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
        let startGap = Math.max(0, firstItem.range.Start - loadZone.Start);
        let endGap = Math.max(0, loadZone.End - lastItem.range.End);
        const expandStartBy = this.renderState.hasVeryFirstItem
            ? 0
            : clamp(Math.ceil(startGap / itemSize), 0, MaxExpandBy);
        const expandEndBy = this.renderState.hasVeryLastItem
            ? 0
            : clamp(Math.ceil(endGap / itemSize), 0, MaxExpandBy);
        const keyRange = new Range(firstItem.Key, lastItem.Key);
        const query = new VirtualListDataQuery(keyRange);
        query.expandStartBy = expandStartBy / responseFulfillmentRatio;
        query.expandEndBy = expandEndBy / responseFulfillmentRatio;

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

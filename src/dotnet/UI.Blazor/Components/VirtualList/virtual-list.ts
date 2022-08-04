import { debounce, Debounced } from 'debounce';
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

const LogScope: string = 'VirtualList';
const ScrollStoppedTimeout: number = 64;
const UpdateClientSideStateTimeout: number = 320;
const IronPantsHandleTimeout: number = 320;
const SizeEpsilon: number = 1;
const EdgeEpsilon: number = 4;
const MaxExpandBy: number = 256;
const StickyEdgeTolerance: number = 50;

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
    private readonly _ironPantsHandlerInterval: number;
    private readonly _bufferZoneSize;
    private readonly _unmeasuredItems: Set<string>;
    private readonly _visibleItems: Set<string>;

    private readonly updateClientSideStateDebounced: Debounced<typeof this.updateClientSideState>;

    private _isDisposed = false;
    private _scrollTopPivotRef: HTMLElement | null = null;
    private _scrollTopPivotOffset: number | null = null;
    private _scrollTopPivotLocation: 'top' | 'bottom' | null = null;
    private _stickyEdge: Required<VirtualListStickyEdgeState> | null = null;
    private _whenRenderCompleted: Promise<void> | null = null;
    private _whenRenderCompletedResolve: () => void | null = null;

    private _isUpdatingClientState: boolean = false;
    private _isRendering: boolean = false;
    private _onScrollStoppedTimeout: any = null;

    private _updateClientSideStateTimeout: number = null;
    private _updateClientSideStateTasks: Promise<void>[] = [];

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
            console.log(`${LogScope}: .ctor`);
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
            this.onIntersect,
            {
                root: null,
                threshold: [0, 0.1, 0.9, 1],
                rootMargin: '10px',
            });

        // @ts-ignore
        this._ironPantsHandlerInterval = setInterval(this.onIronPantsHandle, IronPantsHandleTimeout);

        this._unmeasuredItems = new Set<string>();
        this._visibleItems = new Set<string>();
        this.updateClientSideStateDebounced = debounce(
            onceAtATime(this.updateClientSideState),
            UpdateClientSideStateTimeout,
            true);

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

    get isSafeToScroll(): boolean {
        return this._onScrollStoppedTimeout == null;
    }

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
        clearInterval(this._ironPantsHandlerInterval);
        this._ref.removeEventListener('scroll', this.onScroll);
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private maybeOnRenderEnd = (mutations: MutationRecord[], observer: MutationObserver): void => {
        if (this._debugMode)
            console.log(`${LogScope}.maybeOnRenderEnd: `, mutations.length);

        this._whenRenderCompleted = new Promise<void>(resolve => {
            this._whenRenderCompletedResolve = resolve;
        });

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

        if (!isNodesAdded)
            return;

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
            if (this._unmeasuredItems.size === 0) {
                const rsJson = this._renderStateRef.textContent;
                if (rsJson == null || rsJson === '')
                    return;

                const rs = JSON.parse(rsJson) as Required<VirtualListRenderState>;
                if (rs.renderIndex <= this.renderState.renderIndex)
                    return;

                const riText = this._renderIndexRef.dataset['renderIndex'];
                if (riText == null || riText == '')
                    return;

                const ri = Number.parseInt(riText);
                if (ri != rs.renderIndex)
                    return;

                void this.onRenderEnd(rs);
            }
        }
    }

    private onIntersect = (entries: IntersectionObserverEntry[], observer: IntersectionObserver): void => {
        for (const entry of entries) {
            const itemRef = entry.target as HTMLElement;
            const key = getItemKey(itemRef);
            if (entry.intersectionRatio <= 0) {
                this._visibleItems.delete(key);
            }
            else if (entry.intersectionRatio >= 0.9) {
                this._visibleItems.add(key);
            }
        }
    }

    private async onRenderEnd(rs: Required<VirtualListRenderState>): Promise<void> {
        if (this._isRendering) {
            if (this._debugMode)
                console.warn(`${LogScope}.onRenderEnd - Skipped, renderIndex = #${rs.renderIndex}, rs =`, rs);
            return;
        }

        if (this._debugMode)
            console.log(`${LogScope}.onRenderEnd, renderIndex = #${rs.renderIndex}, rs =`, rs);

        this._isRendering = true;
        try {
            this.renderState = rs;

            // Update statistics
            const ratio = this.statistics.responseFulfillmentRatio;
            if (rs.query.expandStartBy > 0 && !rs.hasVeryFirstItem)
                this.statistics.addResponse(rs.startExpansion, rs.query.expandStartBy * ratio);
            if (rs.query.expandEndBy > 0 && !rs.hasVeryLastItem)
                this.statistics.addResponse(rs.endExpansion, rs.query.expandEndBy * ratio);

            let isScrollHappened = false;
            let scrollToItemRef = this.getItemRef(rs.scrollToKey);
            let lastVisibleItemScrollOffset: number = null;
            let lastVisibleItemRef: HTMLElement = null;
            await new Promise<void>(resolve => {
                requestAnimationFrame(time => {
                    if (this.clientSideState.visibleKeys.length > 0) {
                        const key = this.clientSideState.visibleKeys[this.clientSideState.visibleKeys.length - 1];
                        lastVisibleItemRef = this.getItemRef(key);
                        if (lastVisibleItemRef) {
                            const itemY0 = this.getItemY0();
                            const scrollTop = this.getScrollTop();
                            const itemRect = lastVisibleItemRef.getBoundingClientRect();
                            lastVisibleItemScrollOffset = itemRect.y - itemY0 - scrollTop;
                        }
                    }

                    for (const itemRef of this.getNewItemRefs()) {
                        itemRef.classList.remove('new');
                    }

                    resolve();
                });
            });

            if (scrollToItemRef != null) {
                // Server-side scroll request
                if (!this.isItemFullyVisible(scrollToItemRef)) {
                    if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                        this.scrollTo(scrollToItemRef, false, 'end');
                        this.setStickyEdge({ itemKey: rs.scrollToKey, edge: VirtualListEdge.End });
                    } else {
                        this.scrollTo(scrollToItemRef, true, 'center');
                    }
                    isScrollHappened = true;
                }
                else if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                    this.setStickyEdge({ itemKey: rs.scrollToKey, edge: VirtualListEdge.End });
                }
            } else if (this.isSafeToScroll && this._stickyEdge != null) {
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
                        isScrollHappened = true;
                    }
                }
            }
            else if (this._scrollTopPivotRef != null) {
                // _scrollTopPivot handling

                const viewportHeight = this._ref.clientHeight;
                if (this._scrollTopPivotLocation === 'top') {
                    if (this._scrollTopPivotOffset - viewportHeight > 0) {
                        // item was out of viewport
                        this.scrollTo(this._scrollTopPivotRef, false, 'end');
                        isScrollHappened = true;
                    }
                    else {
                        const itemY0 = this.getItemY0();
                        const scrollTop = this.getScrollTop();
                        const itemRect = this._scrollTopPivotRef.getBoundingClientRect();
                        const newScrollTopPivotOffset = itemRect.y - itemY0 - scrollTop;
                        let dScrollTop = newScrollTopPivotOffset - this._scrollTopPivotOffset;
                        const newScrollTop = scrollTop + dScrollTop;
                        if (this._debugMode)
                            console.warn(`${LogScope}.onRenderEnd: resync scrollTop: ${scrollTop} + ${dScrollTop} -> ${newScrollTop}`);
                        if (Math.abs(dScrollTop) > SizeEpsilon) {
                            this.setScrollTop(newScrollTop);
                            isScrollHappened = true;
                        }
                    }
                }
                else {
                    if (this._scrollTopPivotOffset + viewportHeight < 0) {
                        // item was out of viewport
                        this.scrollTo(this._scrollTopPivotRef, false, 'start');
                        isScrollHappened = true;
                    }
                    else {
                        const itemY0 = this.getItemY0();
                        const scrollTop = this.getScrollTop();
                        const itemRect = this._scrollTopPivotRef.getBoundingClientRect();
                        const newScrollTopPivotOffset = itemRect.y - itemY0 - scrollTop;
                        let dScrollTop = newScrollTopPivotOffset - this._scrollTopPivotOffset;
                        const newScrollTop = scrollTop + dScrollTop;
                        if (this._debugMode)
                            console.warn(`${LogScope}.onRenderEnd: resync scrollTop: ${scrollTop} + ${dScrollTop} -> ${newScrollTop}`);
                        if (Math.abs(dScrollTop) > SizeEpsilon) {
                            this.setScrollTop(newScrollTop);
                            isScrollHappened = true;
                        }
                    }
                }
            }
            else {
                if (lastVisibleItemRef) {
                    const itemY0 = this.getItemY0();
                    const scrollTop = this.getScrollTop();
                    const itemRect = lastVisibleItemRef.getBoundingClientRect();
                    const newLastVisibleItemScrollOffset = itemRect.y - itemY0 - scrollTop;
                    let dScrollTop = newLastVisibleItemScrollOffset - lastVisibleItemScrollOffset;
                    const newScrollTop = scrollTop + dScrollTop;
                    if (Math.abs(dScrollTop) > SizeEpsilon) {
                        this.setScrollTop(newScrollTop);
                        isScrollHappened = true;
                    }
                }
            }

            if (this._debugMode)
                console.log(`${LogScope}.onRenderEnd - isScrollHappened: ${isScrollHappened}`);
            if (isScrollHappened) {
                // wait for render
                for (let i = 0; i < 3; i++) {
                    await new Promise<void>(resolve => {
                        requestAnimationFrame(time => {
                            resolve();
                        });
                    });
                }
                this.renewStickyEdge();
            }

            this._scrollTopPivotRef = null;
            this._scrollTopPivotOffset = null;
        } finally {
            this._isRendering = false;
            if (this._whenRenderCompletedResolve) {
                this._whenRenderCompletedResolve();
                this._whenRenderCompletedResolve = null;
            }
            this.updateClientSideStateDebounced.cancel();
            this.updateClientSideStateDebounced();
        }
    }

    private async updateClientSideState(): Promise<void> {
        const rs = this.renderState;
        if (this._isDisposed || this._isUpdatingClientState || this._isRendering)
            return;

        const whenRenderCompleted = this._whenRenderCompleted;
        if (whenRenderCompleted) {
            await whenRenderCompleted;
        }

        this._lastPlan = this._plan;
        try {
            this._isUpdatingClientState = true;
            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideStateImpl: #${rs.renderIndex}`);

            const state = await new Promise<VirtualListClientSideState | null>(resolve => {
                let state: VirtualListClientSideState = null;
                requestAnimationFrame(time => {
                    try {
                        const viewportHeight = this._ref.clientHeight;
                        const scrollTop = this.getScrollTop();

                        const itemY0 = this.getItemY0();
                        const contentScrollHeight = this._ref.scrollHeight - this._spacerRef.clientHeight - this._endSpacerRef.clientHeight;
                        const contentScrollBottom = scrollTop + viewportHeight;
                        this._scrollTopPivotLocation = null;
                        this._scrollTopPivotRef = null;
                        if (scrollTop < EdgeEpsilon) {
                            // Spacer overlaps with the top of the viewport
                            const itemRef = this.getFirstItemRef();
                            if (scrollTop < 0) {
                                this._scrollTopPivotRef = itemRef;
                                this._scrollTopPivotOffset = -scrollTop;
                                this._scrollTopPivotLocation = 'top';
                            }
                        } else if (contentScrollBottom - contentScrollHeight > EdgeEpsilon) {
                            // End spacer overlaps with the bottom of the viewport
                            const itemRef = this.getLastItemRef();
                            const itemRect = itemRef.getBoundingClientRect();
                            const y = itemRect.y - itemY0;
                            if (y <= contentScrollBottom) {
                                this._scrollTopPivotRef = itemRef;
                                this._scrollTopPivotOffset = y - scrollTop;
                                this._scrollTopPivotLocation = 'bottom';
                            }
                        }

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
                    console.log(`${LogScope}.updateClientSideStateImpl: state:`, state);
                const expectedRenderIndex = this.renderState.renderIndex;
                if (state.renderIndex != expectedRenderIndex) {
                    return;
                }

                this.clientSideState = state;
                if (state.visibleKeys.length > 0) {
                    if (this._debugMode)
                        console.log(
                            `${LogScope}.updateClientSideStateImpl: server call UpdateVisibleKeys:`,
                            state.visibleKeys);

                    await this._blazorRef.invokeMethodAsync('UpdateVisibleKeys', state.visibleKeys);
                }

                const plan = this._plan = this._lastPlan.next();
                if (!plan.isFullyLoaded) {
                    await this.requestData();
                }
            }
        } finally {
            this._isUpdatingClientState = false;
        }
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
            this.onIntersect(intersections, this._visibilityObserver);
        }
    }

    private onScroll = (): void => {
        if (this._isRendering || this._isDisposed)
            return;

        if (this._onScrollStoppedTimeout != null)
            clearTimeout(this._onScrollStoppedTimeout);
        this._onScrollStoppedTimeout =
            setTimeout(() => {
                this._onScrollStoppedTimeout = null;
                this.updateClientSideStateDebounced();
            }, ScrollStoppedTimeout);
    };

    private getNewItemRefs(): IterableIterator<HTMLElement> {
        // getElementsByClassName is faster than querySelectorAll
        return Array.from(this._containerRef.getElementsByClassName('item new')).values() as IterableIterator<HTMLElement>;
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

    private getItemY0(): number {
        return this._spacerRef.getBoundingClientRect().bottom;
    }

    private isItemFullyVisible(itemRef: HTMLElement): boolean {
        const itemRect = itemRef.getBoundingClientRect();
        const viewRect = this._ref.getBoundingClientRect();
        return itemRect.top >= viewRect.top && itemRect.top <= viewRect.bottom
            && itemRect.bottom >= viewRect.top && itemRect.bottom <= viewRect.bottom;
    }

    private getScrollTop(): number {
        const scrollHeight = this._ref.scrollHeight;
        const spacerHeight = this._spacerRef.clientHeight;
        let scrollTop = this._ref.scrollTop;
        const viewportHeight = this._ref.clientHeight;
        scrollTop += scrollHeight - viewportHeight;
        scrollTop -= spacerHeight;
        return scrollTop;
    }

    private setScrollTop(scrollTop: number): void {
        const spacerHeight = this._spacerRef.clientHeight;
        scrollTop += spacerHeight;
        const viewportHeight = this._ref.clientHeight;
        const scrollBottom = scrollTop + viewportHeight;
        const scrollHeight = this._ref.scrollHeight;
        scrollTop = scrollBottom - scrollHeight;
        this._ref.scrollTop = scrollTop;
    }

    private scrollTo(
        itemRef?: HTMLElement,
        useSmoothScroll: boolean = false,
        blockPosition: ScrollLogicalPosition = 'nearest') {
        if (this._debugMode)
            console.warn(`${LogScope}.scrollTo, item key =`, getItemKey(itemRef));
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

    private renewStickyEdge(): boolean {
        const viewRect = this._ref.getBoundingClientRect();
        if (this._debugMode)
            console.info(`${LogScope}.renewStickyEdge`);
        for (const stickyEdge of this.getStickyEdgeCandidates()) {
            if (stickyEdge.itemKey == null)
                return;
            const itemRef = this.getItemRef(stickyEdge.itemKey);
            if (isPartiallyVisible(itemRef.getBoundingClientRect(), viewRect, StickyEdgeTolerance))
                return this.setStickyEdge(stickyEdge);
        }
        return this.setStickyEdge(null);
    }

    private* getStickyEdgeCandidates(): IterableIterator<VirtualListStickyEdgeState> {
        const rs = this.renderState;
        if (rs.hasVeryFirstItem)
            yield { itemKey: this.getFirstItemKey(), edge: VirtualListEdge.Start };
        if (rs.hasVeryLastItem)
            yield { itemKey: this.getLastItemKey(), edge: VirtualListEdge.End };
    }

    private async requestData(): Promise<void> {
        if (this._plan.isFullyLoaded)
            return;

        this._query = this.getDataQuery();
        if (this._query.isSimilarTo(this._lastQuery))
            return;

        if (this._debugMode)
            console.warn(`${LogScope}.requestData: query:`, this._query);
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
        if (loadZone.Start < alreadyLoaded.Start && (viewport.Start - alreadyLoaded.Start > viewportSize * 2))
            return this._lastQuery;
        if (loadZone.End > alreadyLoaded.End && (alreadyLoaded.End - viewport.End > viewportSize * 2))
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

function isPartiallyVisible(rect: DOMRect, viewRect: DOMRect, tolerance: number = 0): boolean {
    return rect.bottom > viewRect.top - tolerance && rect.top < viewRect.bottom + tolerance;
}

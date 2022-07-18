import './virtual-list.css';
import { delayAsync } from 'delay';
import { VirtualListClientSideState } from './ts/virtual-list-client-side-state';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListStickyEdgeState } from './ts/virtual-list-sticky-edge-state';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListRenderPlan } from './ts/virtual-list-render-plan';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { Range } from './ts/range';
import { VirtualListStatistics } from './ts/virtual-list-statistics';
import { VirtualListAccessor } from './ts/virtual-list-accessor';
import { Clamp } from './ts/math';
import { RangeExt } from './ts/range-ext';

const LogScope: string = 'VirtualList';
const ScrollStoppedTimeout: number = 64;
const UpdateClientSideStateTimeout: number = 320;
const RenderToUpdateClientSideStateDelay: number = 20;
const SizeEpsilon: number = 1;
const ItemSizeEpsilon: number = 1;
const MoveSizeEpsilon: number = 28;
const EdgeEpsilon: number = 4;

export class VirtualList implements VirtualListAccessor {
    private readonly _debugMode: boolean = false;
    private readonly _isEndAligned: boolean = false;
    /** ref to div.virtual-list */
    private readonly _ref: HTMLElement;
    private readonly _renderStateRef: HTMLElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _spacerRef: HTMLElement;
    private readonly _endSpacerRef: HTMLElement;
    private readonly _renderIndexRef: HTMLElement;
    private readonly _abortController: AbortController;
    private readonly _renderEndObserver: MutationObserver;
    private _isDisposed = false;
    private _scrollTopPivotRef?: HTMLElement;
    private _scrollTopPivotOffset?: number;
    private _scrollTopPivotLocation?: 'visible' | 'top' | 'bottom' = 'visible';
    private _stickyEdge?: Required<VirtualListStickyEdgeState> = null;

    private _isUpdatingClientState: boolean = false;
    private _isRendering: boolean = false;
    private _onScrollStoppedTimeout: any = null;

    private _updateClientSideStateTimeout: number = null;
    private _updateClientSideStateTasks: Promise<void>[] = [];
    private _updateClientSideStateRenderIndex = -1;

    private LastPlan?: VirtualListRenderPlan = null;
    private Plan: VirtualListRenderPlan;
    private LastQuery: VirtualListDataQuery = VirtualListDataQuery.None;
    private Query: VirtualListDataQuery = VirtualListDataQuery.None;
    private VisibleKeysState: string[] = [];
    private MaxExpandBy: number = 256;

    public RenderState: VirtualListRenderState;
    public ClientSideState: VirtualListClientSideState;
    public Statistics: VirtualListStatistics = new VirtualListStatistics();
    public LoadZoneSize;
    public BufferZoneSize;
    public AlignmentEdge: VirtualListEdge = VirtualListEdge.End;

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        isEndAligned: boolean,
        loadZoneSize: number,
        bufferZoneSize: number,
        debugMode: boolean) {
        if (debugMode) {
            console.log(`${LogScope}: .ctor`);
            window['virtualList'] = this;
        }

        this._debugMode = debugMode;
        this._isEndAligned = isEndAligned;
        this.LoadZoneSize = loadZoneSize;
        this.BufferZoneSize = bufferZoneSize;
        this._ref = ref;
        this._blazorRef = backendRef;
        this._isDisposed = false;
        this._abortController = new AbortController();
        this._spacerRef = this._ref.querySelector(':scope > .spacer-start');
        this._endSpacerRef = this._ref.querySelector(':scope > .spacer-end');
        this._renderStateRef = this._ref.querySelector(':scope > .data.render-state');
        this._renderIndexRef = this._ref.querySelector(':scope > .data.render-index');

        // Events & observers
        const listenerOptions = { signal: this._abortController.signal };
        this._ref.addEventListener('scroll', this.onScroll, listenerOptions);
        this._renderEndObserver = new MutationObserver(this.maybeOnRenderEnd);
        this._renderEndObserver.observe(
            this._renderIndexRef,
            { attributes: true, attributeFilter: ['data-render-index'] });

        this.RenderState = {
            renderIndex: -1,
            query: VirtualListDataQuery.None,

            spacerSize: 0,
            endSpacerSize: 0,
            startExpansion: 0,
            endExpansion: 0,
            hasVeryFirstItem: false,
            hasVeryLastItem: false,

            scrollToKey: null,
            useSmoothScroll: false,
            items: {}
        };
        this.ClientSideState = {
            renderIndex: 0,
            spacerSize: 0,
            endSpacerSize: 0,
            scrollTop: 0,
            scrollHeight: 0,
            viewportHeight: 0,

            isStickyEdgeChanged: false,
            isUserScrollDetected: false,
            isViewportChanged: false,

            visibleKeys: [],
            items: {}
        }

        this.Plan = new VirtualListRenderPlan(this);
        // this.maybeOnRenderEnd([], this._renderEndObserver);
    };

    get isSafeToScroll(): boolean {
        return this._onScrollStoppedTimeout == null;
    }

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        isEndAligned: boolean,
        loadZoneSize: number,
        bufferZoneSize: number,
        debugMode: boolean) {
        return new VirtualList(ref, backendRef, isEndAligned, loadZoneSize, bufferZoneSize, debugMode);
    }

    public dispose() {
        this._isDisposed = true;
        this._abortController.abort();
        this._renderEndObserver.disconnect();
        this._ref.removeEventListener('scroll', this.onScroll);
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private maybeOnRenderEnd = (mutations: MutationRecord[], observer: MutationObserver): void => {
        if (this._debugMode)
            console.log(`${LogScope}.maybeOnRenderEnd:`, mutations);

        const rsDataRef = this.getRenderStateDataRef();
        if (rsDataRef == null)
            return;

        const rsJson = rsDataRef.textContent;
        if (rsJson == null || rsJson === '')
            return;

        const rs = JSON.parse(rsJson) as Required<VirtualListRenderState>;
        if (rs.renderIndex <= this.RenderState.renderIndex)
            return;

        const riText = this._renderIndexRef.dataset['renderIndex'];
        if (riText == null || riText == '')
            return;

        const ri = Number.parseInt(riText);
        if (ri != rs.renderIndex)
            return;
        void this.onRenderEnd(rs, mutations.length == 0);
    };

    private async onRenderEnd(rs: Required<VirtualListRenderState>, isFirstRender: boolean): Promise<void> {
        this._isRendering = true;
        try {
            this.RenderState = rs;
            if (this._debugMode)
                console.log(`${LogScope}.onRenderEnd, renderIndex = #${rs.renderIndex}, rs =`, rs);

            let isScrollHappened = false;
            let scrollToItemRef = this.getItemRef(rs.scrollToKey);
            if (scrollToItemRef != null) {
                // Server-side scroll request
                if (!this.isItemVisible(scrollToItemRef)) {
                    this.scrollTo(scrollToItemRef, rs.useSmoothScroll, 'center');
                    isScrollHappened = true;
                }
                this._scrollTopPivotRef = null;
                this._scrollTopPivotOffset = null;
            } else if (this.isSafeToScroll && this._stickyEdge != null) {
                // Sticky edge scroll
                const itemKey = this._stickyEdge?.Edge === VirtualListEdge.Start
                    ? this.getFirstItemKey()
                    : this.getLastItemKey();
                if (itemKey == null) {
                    this.setStickyEdge(null);
                } else if (itemKey !== this._stickyEdge.ItemKey) {
                    this.setStickyEdge({ ItemKey: itemKey, Edge: this._stickyEdge.Edge });
                    let itemRef = this.getItemRef(itemKey);
                    this.scrollTo(itemRef, true);
                    isScrollHappened = true;
                }
                this._scrollTopPivotRef = null;
                this._scrollTopPivotOffset = null;
            } else if (this._scrollTopPivotRef != null) {
                // _scrollTopPivot handling
                if (this._scrollTopPivotLocation === 'visible') {
                    const isPivotVisibleNow = this.isItemVisible(this._scrollTopPivotRef);
                    if (isPivotVisibleNow) {
                        if (this._debugMode)
                            console.log(`${LogScope}.onRenderEnd - pivot is visible`);
                    }
                    else {
                        if (this._debugMode)
                            console.log(`${LogScope}.onRenderEnd - pivot become invisible`);
                        const itemY0 = this.getItemY0();
                        const scrollTop = this.getScrollTop();
                        const newScrollTopPivotOffset = this._scrollTopPivotRef.getBoundingClientRect().y - itemY0 - scrollTop;
                        const dScrollTop = newScrollTopPivotOffset - this._scrollTopPivotOffset;
                        const newScrollTop = scrollTop + dScrollTop;
                        if (this._debugMode)
                            console.warn(`${LogScope}.onRenderEnd: resync scrollTop: ${scrollTop} + ${dScrollTop} -> ${newScrollTop}`);
                        if (Math.abs(dScrollTop) > SizeEpsilon) {
                            this.setScrollTop(newScrollTop);
                            isScrollHappened = true;
                        }
                        this._scrollTopPivotRef = null;
                        this._scrollTopPivotOffset = null;
                    }
                } else {
                    let itemRef = this._scrollTopPivotRef;
                    const position: ScrollLogicalPosition = this._scrollTopPivotLocation === 'bottom'
                        ? 'end'
                        : 'start';
                    if (this._isEndAligned && position === 'start' || !this._isEndAligned && position === 'end') {
                        const itemY0 = this.getItemY0();
                        const scrollTop = this.getScrollTop();
                        const newScrollTopPivotOffset = this._scrollTopPivotRef.getBoundingClientRect().y - itemY0 - scrollTop;
                        const dScrollTop = newScrollTopPivotOffset - this._scrollTopPivotOffset;
                        const newScrollTop = scrollTop + dScrollTop;
                        if (this._debugMode)
                            console.warn(`${LogScope}.onRenderEnd: resync scrollTop: ${scrollTop} + ${dScrollTop} -> ${newScrollTop}`);
                        if (Math.abs(dScrollTop) > SizeEpsilon) {
                            this.setScrollTop(newScrollTop);
                            isScrollHappened = true;
                        }
                        this._scrollTopPivotRef = null;
                        this._scrollTopPivotOffset = null;
                    }
                    else {
                        this.scrollTo(itemRef, false, position);
                        isScrollHappened = true;
                        this._scrollTopPivotRef = null;
                        this._scrollTopPivotOffset = null;
                    }
                }
            }

            if (this._debugMode)
                console.log(`${LogScope}.onRenderEnd - ` +
                    `ClientSideState.scrollHeight: #${this.ClientSideState.scrollHeight}; ClientSideState.scrollTop: #${this.ClientSideState.scrollTop}; scrollTop: #${this.getScrollTop()}; ` +
                    `pivotRefKey: ${getItemKey(this._scrollTopPivotRef)}; pivotOffset: ${this._scrollTopPivotOffset}; ` +
                    `isScrollHappened: ${isScrollHappened}`);

            if (isScrollHappened)
                await delayAsync(RenderToUpdateClientSideStateDelay);
        } finally {
            this._isRendering = false;
            if (rs.renderIndex < this._updateClientSideStateRenderIndex) {
                // This is an outdated update already
                if (this._debugMode)
                    console.log(`${LogScope}.onRenderEnd skips updateClientSideStateDebounced:` +
                        ` #${rs.renderIndex} < #${this._updateClientSideStateRenderIndex}`);
                // such an update will be ignored anyway
            }
            else {
                 if (!isFirstRender) {
                    this.updateClientSideStateDebounced(true);
                 }
            }
        }
    }

    private updateClientSideStateDebounced(immediately: boolean = false) {
        if (this._isRendering)
            return;

        if (immediately) {
            if (this._updateClientSideStateTimeout != null) {
                clearTimeout(this._updateClientSideStateTimeout);
                this._updateClientSideStateTimeout = null;
            }
            void this.updateClientSideState();
        } else {
            if (this._updateClientSideStateTimeout != null)
                return;
            this._updateClientSideStateTimeout = self.setTimeout(async () => {
                this._updateClientSideStateTimeout = null;
                await this.updateClientSideState();
            }, UpdateClientSideStateTimeout);
        }
    }

    private async updateClientSideState(): Promise<void> {
        const queue = this._updateClientSideStateTasks;
        const lastTask = queue.length > 0 ? queue[queue.length - 1] : null;
        if (queue.length >= 2) {
            await lastTask;
            return;
        }
        const newTask = (async () => {
            try {
                if (lastTask != null)
                    await lastTask.then(v => v, _ => null);
                await this.updateClientSideStateImpl();
            } finally {
                void queue.shift();
            }
        })();
        queue.push(newTask);
    }

    private async updateClientSideStateImpl(): Promise<void> {
        const rs = this.RenderState;
        const cs = this.ClientSideState;
        if (this._isDisposed || this._isUpdatingClientState || rs.renderIndex === 0)
            return;

        this.LastPlan = this.Plan;
        try {
            this._isUpdatingClientState = true;
            if (rs.renderIndex < this._updateClientSideStateRenderIndex) {
                // This update will be dropped by server
                if (this._debugMode)
                    console.log(`${LogScope}.updateClientSideStateImpl: skipped for` +
                                    ` #${rs.renderIndex} < #${this._updateClientSideStateRenderIndex}`);
                return; // This update was already pushed
            }

            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideStateImpl: #${rs.renderIndex}`);

            const state = await new Promise<VirtualListClientSideState | null>(resolve => {
                let state: VirtualListClientSideState = null;
                requestAnimationFrame(time => {
                    try {
                        const spacerRect = this._spacerRef.getBoundingClientRect();
                        const endSpacerRect = this._endSpacerRef.getBoundingClientRect();

                        const spacerSize = spacerRect.height;
                        const endSpacerSize = endSpacerRect.height;
                        const scrollHeight = this._ref.scrollHeight;
                        const viewportHeight = this._ref.clientHeight;
                        const scrollTop = this.getScrollTop();

                        const itemY0 = this.getItemY0();
                        const contentScrollHeight = this._ref.scrollHeight - this._spacerRef.clientHeight - this._endSpacerRef.clientHeight;
                        const contentScrollBottom = scrollTop + viewportHeight;
                        this._scrollTopPivotLocation = 'visible';
                        this._scrollTopPivotRef = null;
                        if (scrollTop < EdgeEpsilon) {
                            // Spacer overlaps with the top of the viewport
                            const itemRefs = this.getOrderedItemRefs();
                            for (const itemRef of itemRefs) {
                                const key = getItemKey(itemRef);
                                const knownItem = rs.items[key];
                                if (!knownItem)
                                    continue; // Item won't exist once rendering completes

                                const y = itemRef.getBoundingClientRect().y - itemY0;
                                if (y >= scrollTop) {
                                    this._scrollTopPivotRef = itemRef;
                                    this._scrollTopPivotOffset = y - scrollTop;
                                    this._scrollTopPivotLocation = 'bottom';
                                    break;
                                }
                            }
                        } else if (contentScrollHeight < contentScrollBottom + EdgeEpsilon) {
                            // End spacer overlaps with the bottom of the viewport
                            const itemRefs = this.getOrderedItemRefs(true);
                            for (const itemRef of itemRefs) {
                                const key = getItemKey(itemRef);
                                const knownItem = rs.items[key];
                                if (!knownItem)
                                    continue; // Item won't exist once rendering completes

                                const y = itemRef.getBoundingClientRect().y - itemY0;
                                if (y <= contentScrollBottom) {
                                    this._scrollTopPivotRef = itemRef;
                                    this._scrollTopPivotOffset = y - scrollTop;
                                    this._scrollTopPivotLocation = 'top';
                                    break;
                                }
                            }
                        }
                        if (this._scrollTopPivotRef) {
                            const itemRef = this._scrollTopPivotRef;
                            if (this.isItemVisible(itemRef)) {
                                this._scrollTopPivotLocation = 'visible';
                            }
                        }

                        state = {
                            renderIndex: rs.renderIndex,

                            spacerSize: spacerSize,
                            endSpacerSize: endSpacerSize,
                            scrollHeight: scrollHeight,
                            scrollTop: scrollTop,
                            viewportHeight: viewportHeight,
                            stickyEdge: this._stickyEdge,
                            scrollAnchorKey: this._scrollTopPivotRef ? getItemKey(this._scrollTopPivotRef) : null,

                            items: {}, // Will be updated further
                            visibleKeys: [],

                            isViewportChanged: false, // Will be updated further
                            isStickyEdgeChanged: false, // Will be updated further
                            isUserScrollDetected: false, // Will be updated further
                        } as VirtualListClientSideState;

                        let gotResizedItems = false;
                        const visibleItemKeys = [];
                        for (const itemRef of this.getItemRefs()) {
                            const key = getItemKey(itemRef);
                            const knownItem = cs.items[key];
                            const knownSize = knownItem?.size ?? -1;
                            const size = itemRef.getBoundingClientRect().height;
                            //TODO(AK): Optimize size measurements with Resize Observer API + dataHash comparison
                            //TODO(AK): Optimize visibility tracking with Intersection Observer API
                            const isVisible = this.isItemVisible(itemRef);
                            if (isVisible) {
                                visibleItemKeys.push(key);
                            }
                            if (knownSize < 0) {
                                state.items[key] = {
                                    size,
                                    dataHash: rs.items[key]?.dataHash ?? 0,
                                };
                            } else if (Math.abs(size - knownSize) > ItemSizeEpsilon) {
                                state.items[key] = {
                                    size,
                                    dataHash: rs.items[key]?.dataHash ?? 0,
                                };
                                gotResizedItems = true;
                            }
                        }
                        state.visibleKeys = visibleItemKeys;

                        const hasItemSizes = Object.keys(state.items).length > 0 || Object.keys(cs.items).length > 0;
                        const isFirstRender = rs.renderIndex <= 2;
                        const isScrollHappened = hasItemSizes && cs.scrollTop != null &&  Math.abs(state.scrollTop - cs.scrollTop) > MoveSizeEpsilon;
                        const isScrollTopChanged = cs.scrollTop == null || Math.abs(state.scrollTop - cs.scrollTop) > MoveSizeEpsilon;
                        const isScrollHeightChanged = cs.scrollHeight == null || Math.abs(state.scrollHeight - cs.scrollHeight) > MoveSizeEpsilon;
                        const isViewportHeightChanged = cs.viewportHeight == null || Math.abs(state.viewportHeight - cs.viewportHeight) > MoveSizeEpsilon;

                        state.isViewportChanged = isScrollTopChanged || isScrollHeightChanged || isViewportHeightChanged;
                        state.isUserScrollDetected = isScrollHappened && !gotResizedItems;
                        if (state.isUserScrollDetected || isFirstRender || endSpacerSize == 0 || spacerSize == 0)
                            this.renewStickyEdge();
                        state.isStickyEdgeChanged =
                            cs.stickyEdge?.ItemKey !== state.stickyEdge?.ItemKey
                            || cs.stickyEdge?.Edge !== state.stickyEdge?.Edge;

                        if (this._debugMode) {
                            console.log(`${LogScope}.updateClientSideStateImpl: changes:` +
                                            (Object.keys(state.items).length > 0 ? ' [items]' : '') +
                                            (state.isUserScrollDetected ? ' [user scroll]' : '') +
                                            (state.isViewportChanged ? ' [viewport]' : '') +
                                            (state.isStickyEdgeChanged ? ' [sticky edge]' : ''));
                            if (state.isViewportChanged)
                                console.log(`${LogScope}.updateClientSideStateImpl: viewport change:` +
                                                (isScrollTopChanged
                                                 ? ` [scrollTop: ${cs.scrollTop} -> ${state.scrollTop}]`
                                                 : '') +
                                                (isScrollHeightChanged
                                                 ? ` [scrollHeight: ${cs.scrollHeight} -> ${state.scrollHeight}]`
                                                 : '') +
                                                (isViewportHeightChanged
                                                 ? ` [viewportHeight: ${cs.viewportHeight} -> ${state.viewportHeight}]`
                                                 : ''));
                        }

                        // const mustUpdateClientSideState = state.isViewportChanged;
                        const mustUpdateClientSideState =
                            state.isViewportChanged
                            || state.isStickyEdgeChanged
                            || Object.keys(state.items).length > 0;
                        if (!mustUpdateClientSideState) {
                            // if (this._debugMode)
                            //     console.log(`${LogScope}.updateClientSideStateImpl: server call skipped`);
                            state = null;
                        }
                    } finally {
                        resolve(state);
                    }
                });
            });

            if (state) {
                if (this._debugMode)
                    console.log(`${LogScope}.updateClientSideStateImpl: state:`, state);
                const expectedRenderIndex = this.RenderState.renderIndex;
                if (state.renderIndex != expectedRenderIndex) {
                    return;
                }

                this.ClientSideState = state;
                const newVisibleKeys = state.visibleKeys;
                if (newVisibleKeys?.length > 0 && this.VisibleKeysState != null)
                    // TODO(AK): Push visible keys to Blazor
                    this.VisibleKeysState = newVisibleKeys;

                const plan = this.Plan = this.LastPlan.Next();
                if (!plan.IsFullyLoaded) {
                    await this.requestData();
                }
                if (state.renderIndex > this._updateClientSideStateRenderIndex)
                {
                    this._updateClientSideStateRenderIndex = state.renderIndex;
                }
            }
        } finally {
            this._isUpdatingClientState = false;
        }
    }

    // Event handlers

    private onScroll = (): void => {
        if (this._isRendering || this._isDisposed)
            return;

        if (this._onScrollStoppedTimeout != null)
            clearTimeout(this._onScrollStoppedTimeout);
        this._onScrollStoppedTimeout =
            setTimeout(() => {
                this._onScrollStoppedTimeout = null;
                this.updateClientSideStateDebounced(true);
            }, ScrollStoppedTimeout);
        // this.updateClientSideStateDebounced();
    };

    private getRenderStateDataRef(): HTMLElement {
        return this._renderStateRef.querySelector(':scope > .data.render-state-data');
    }

    private getItemRefs(): IterableIterator<HTMLElement> {
        return this._ref.querySelectorAll(':scope > .item').values() as IterableIterator<HTMLElement>;
    }

    private getOrderedItemRefs(bottomToTop: boolean = false): IterableIterator<HTMLElement> {
        const itemRefs = this.getItemRefs();
        if (this._isEndAligned == bottomToTop)
            return itemRefs; // No need to reorder
        const result = Array.from(itemRefs);
        result.reverse();
        return result.values();
    }

    private getItemRef(key: string): HTMLElement | null {
        if (key == null || key == "")
            return null;
        return this._ref.querySelector(`:scope > .item[data-key="${key}"]`);
    }

    private getFirstItemRef(): HTMLElement | null {
        const itemRef = this._isEndAligned
            ? this._spacerRef.previousElementSibling
            : this._spacerRef.nextElementSibling;
        if (itemRef == null || !itemRef.classList.contains('item'))
            return null;
        return itemRef as HTMLElement;
    }

    private getFirstItemKey(): string | null {
        return getItemKey(this.getFirstItemRef());
    }

    private getLastItemRef(): HTMLElement | null {
        const itemRef = this._isEndAligned
            ? this._endSpacerRef.nextElementSibling
            : this._endSpacerRef.previousElementSibling;
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

    private isItemVisible(itemRef: HTMLElement): boolean {
        const itemRect = itemRef.getBoundingClientRect();
        const viewRect = this._ref.getBoundingClientRect();
        if (itemRect.bottom <= viewRect.top)
            return false;
        return itemRect.top < viewRect.bottom;

    }

    private getScrollTop(): number {
        const scrollHeight = this._ref.scrollHeight;
        const spacerHeight = this._spacerRef.clientHeight;
        let scrollTop = this._ref.scrollTop;
        if (this._isEndAligned) {
            const viewportHeight = this._ref.clientHeight;
            scrollTop += scrollHeight - viewportHeight;
        }
        scrollTop -= spacerHeight;
        return scrollTop;
    }

    private setScrollTop(scrollTop: number): void {
        const spacerHeight = this._spacerRef.clientHeight;
        scrollTop += spacerHeight;
        if (this._isEndAligned) {
            const viewportHeight = this._ref.clientHeight;
            const scrollBottom = scrollTop + viewportHeight;
            const scrollHeight = this._ref.scrollHeight;
            scrollTop = scrollBottom - scrollHeight;
        }
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
        if (old?.ItemKey !== stickyEdge?.ItemKey || old?.Edge !== stickyEdge?.Edge) {
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
            if (stickyEdge.ItemKey == null)
                return;
            const itemRef = this.getItemRef(stickyEdge.ItemKey);
            if (isPartiallyVisible(itemRef.getBoundingClientRect(), viewRect))
                return this.setStickyEdge(stickyEdge);
        }
        return this.setStickyEdge(null);
    }

    private* getStickyEdgeCandidates(): IterableIterator<VirtualListStickyEdgeState> {
        const rs = this.RenderState;
        if (!this._isEndAligned) {
            if (rs.hasVeryFirstItem)
                yield { ItemKey: this.getFirstItemKey(), Edge: VirtualListEdge.Start };
            if (rs.hasVeryLastItem)
                yield { ItemKey: this.getLastItemKey(), Edge: VirtualListEdge.End };
        } else {
            if (rs.hasVeryLastItem)
                yield { ItemKey: this.getLastItemKey(), Edge: VirtualListEdge.End };
            if (rs.hasVeryFirstItem)
                yield { ItemKey: this.getFirstItemKey(), Edge: VirtualListEdge.Start };
        }
    }

    private async requestData(): Promise<void> {
        if (this.Plan.IsFullyLoaded /*&& this.Data.ScrollToKey != null && this.Data.ScrollToKey.trim().length > 0*/)
            return;

        this.Query = this.getDataQuery();
        if (this.Query.IsSimilarTo(this.LastQuery))
            return;

        if (this._debugMode)
            console.warn(`${LogScope}.requestData: query:`, this.Query);
        await this._blazorRef.invokeMethodAsync('RequestData', this.Query);
        this.LastQuery = this.Query;
    }

    private getDataQuery(): VirtualListDataQuery {
        const plan = this.Plan;
        const rs = this.RenderState;
        const itemSize = this.Statistics.ItemSize;
        const responseFulfillmentRatio = this.Statistics.ResponseFulfillmentRatio;
        const viewport = plan.Viewport;
        const viewportSize = RangeExt.Size(viewport);
        const alreadyLoaded = plan.ItemRange;
        let loadStart = viewport.Start - this.LoadZoneSize;
        if (loadStart < alreadyLoaded.Start && rs.hasVeryFirstItem)
            loadStart = alreadyLoaded.Start;
        let loadEnd = viewport.End + this.LoadZoneSize;
        if (loadEnd > alreadyLoaded.End && rs.hasVeryLastItem)
            loadEnd = alreadyLoaded.End;
        const loadZone = new Range(loadStart, loadEnd);
        const bufferZone = new Range(Math.max(viewport.Start - this.BufferZoneSize, 0), viewport.End + this.BufferZoneSize);

        if (this.ClientSideState.scrollAnchorKey != null) {
            // we should load data near the last available item on scroll into skeleton area
            const key = this.ClientSideState.scrollAnchorKey!;
            const avgItemsPerLoadZone = this.LoadZoneSize / itemSize;
            const anchorKeyRange = new Range(key, key);
            const anchorQuery = new VirtualListDataQuery(anchorKeyRange);
            anchorQuery.ExpandStartBy = avgItemsPerLoadZone / responseFulfillmentRatio;
            anchorQuery.ExpandEndBy = avgItemsPerLoadZone / responseFulfillmentRatio
            return anchorQuery;
        }
        if (plan.HasUnmeasuredItems) // Let's wait for measurement to complete first
            return this.LastQuery;
        if (plan.Items.length == 0) // No entries -> nothing to "align" the query to
            return this.LastQuery;
        if (plan.Viewport == null)
            return this.LastQuery;
        if (RangeExt.Contains(alreadyLoaded, loadZone))
            return this.LastQuery;
        if (loadZone.Start < alreadyLoaded.Start && (viewport.Start - alreadyLoaded.Start > viewportSize * 2))
            return this.LastQuery;
        if (loadZone.End > alreadyLoaded.End && (alreadyLoaded.End - viewport.End > viewportSize * 2))
            return this.LastQuery;

        let startIndex = -1;
        let endIndex = -1;
        const items = plan.Items;
        for (let i = 0; i < items.length; i++) {
            const item = items[i];
            if (item.IsMeasured && RangeExt.Size(RangeExt.IntersectWith(item.Range, bufferZone)) > 0) {
                endIndex = i;
                if (startIndex < 0)
                    startIndex = i;
            }
            else if (startIndex >= 0) {
                break;
            }
        }
        if (startIndex < 0) {
            // No items inside the bufferZone, so we'll take the first or the last item
            startIndex = endIndex = items[0].Range.End < bufferZone.Start ? 0 : items.length - 1;
        }

        const firstItem = items[startIndex];
        const lastItem = items[endIndex];
        const startGap = Math.max(0, firstItem.Range.Start - loadZone.Start);
        const endGap = Math.max(0, loadZone.End - lastItem.Range.End);

        const expandStartBy = this.RenderState.hasVeryFirstItem
            ? 0
            : Clamp(Math.ceil(startGap / itemSize), 0, this.MaxExpandBy);
        const expandEndBy = this.RenderState.hasVeryLastItem
            ? 0
            : Clamp(Math.ceil(endGap / itemSize), 0, this.MaxExpandBy);
        const keyRange = new Range(firstItem.Key, lastItem.Key);
        const query = new VirtualListDataQuery(keyRange);
        query.ExpandStartBy = expandStartBy / responseFulfillmentRatio;
        query.ExpandEndBy = expandEndBy / responseFulfillmentRatio;

        return query;
    }
}

// Helper functions
function getItemKey(itemRef?: HTMLElement): string | null {
    return itemRef?.dataset['key'];
}

function isPartiallyVisible(rect: DOMRect, viewRect: DOMRect): boolean {
    return rect.bottom > viewRect.top && rect.top < viewRect.bottom;
}

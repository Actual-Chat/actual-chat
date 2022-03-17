import './virtual-list.css';
import { delayAsync } from 'delay';
import { nextTickAsync } from 'next-tick';

const LogScope = 'VirtualList';
const ScrollStoppedTimeout = 2000;
const UpdateClientSideStateTimeout = 10;
const RenderToUpdateClientSideStateDelay = 20;
const SizeEpsilon = 1;
const ItemSizeEpsilon = 2;
const MoveSizeEpsilon = 16;
const EdgeEpsilon = 4;

export class VirtualList {
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
    private readonly _resizeObserver: ResizeObserver;
    private readonly _renderEndObserver: MutationObserver;
    private _listResizeEventCount = 0;
    private _isRendering = false;
    private _renderStartScrollTop = 0;
    private _renderEndScrollTop = 0;
    private _scrollTopPivotRef?: HTMLElement;
    private _scrollTopPivotOffset?: number;

    private _renderState: Required<IRenderState>;
    private _nextRenderState?: Required<IRenderState> = null;
    private _isFirstRender = true;
    private _isSafeToScroll = true;
    private _onScrollStoppedTimeout: number = null;

    private _updateClientSideStateTimeout: number = null;
    private _updateClientSideStateTasks: Promise<void>[] = [];
    private _updateClientSideStateRenderIndex = -1;

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        isEndAligned: boolean,
        debugMode: boolean) {
        return new VirtualList(ref, backendRef, isEndAligned, debugMode);
    }

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        isEndAligned: boolean,
        debugMode: boolean) {
        if (debugMode) {
            console.log(`${LogScope}.ctor`);
            window['virtualList'] = this;
        }

        this._debugMode = debugMode;
        this._isEndAligned = isEndAligned;
        this._ref = ref;
        this._blazorRef = backendRef;
        this._abortController = new AbortController();
        this._spacerRef = this._ref.querySelector(':scope > .spacer-start');
        this._endSpacerRef = this._ref.querySelector(':scope > .spacer-end');
        this._renderStateRef = this._ref.querySelector(':scope > .data.render-state');
        this._renderIndexRef = this._ref.querySelector(':scope > .data.render-index');
        // this._renderStateRef = this._ref.previousElementSibling as HTMLElement;
        // this._renderIndexRef = this._ref.nextElementSibling as HTMLElement;

        // Events & observers
        this._resizeObserver = new ResizeObserver(entries => this.onResize(entries));
        const listenerOptions = { signal: this._abortController.signal };
        this._ref.addEventListener('scroll', this.onScroll, listenerOptions);
        this._renderStateRef.addEventListener('DOMNodeRemoved', this.maybeOnRenderStart);
        this._renderEndObserver = new MutationObserver(this.maybeOnRenderEnd);
        this._renderEndObserver.observe(this._renderIndexRef,
            { attributes: true, attributeFilter: ['data-render-index'] });

        this._renderState = {
            renderIndex: -1,

            spacerSize: 0,
            endSpacerSize: 0,
            scrollHeight: null,
            scrollTop: null,
            viewportHeight: null,

            itemSizes: {},

            mustScroll: false,
            notifyWhenSafeToScroll: false,
            hasUnmeasuredItems: true,
        };

        this.maybeOnRenderStart(null);
        void this.onRenderEnd();
    };

    public dispose() {
        this._abortController.abort();
        this._resizeObserver.disconnect();
        this._renderEndObserver.disconnect();
        this._ref.removeEventListener('scroll', this.onScroll);
        this._renderStateRef.removeEventListener('DOMNodeRemoved', this.maybeOnRenderStart);
    }

    private getRenderStateDataRef(): HTMLElement {
        return this._renderStateRef.querySelector(':scope > .data.render-state-data');
    }

    private getItemRefs(): IterableIterator<HTMLElement> {
        return this._ref.querySelectorAll(':scope > .item').values() as IterableIterator<HTMLElement>;
    }

    private getOrderedItemRefs(bottomToTop = false): IterableIterator<HTMLElement> {
        const itemRefs = this.getItemRefs();
        if (this._isEndAligned == bottomToTop)
            return itemRefs; // No need to reorder
        const result = Array.from(itemRefs);
        result.reverse();
        return result.values();
    }

    private getItemY0(): number {
        return this._spacerRef.getBoundingClientRect().bottom;
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

    private setScrollTop(scrollTop: number): number {
        const requestedScrollTop = scrollTop;
        const spacerHeight = this._spacerRef.clientHeight;
        scrollTop += spacerHeight;
        if (this._isEndAligned) {
            const viewportHeight = this._ref.clientHeight;
            const scrollBottom = scrollTop + viewportHeight;
            const scrollHeight = this._ref.scrollHeight;
            scrollTop = scrollBottom - scrollHeight;
        }
        let actualScrollTop = this.getScrollTop();
        if (this._debugMode)
            console.warn(`${LogScope}.setScrollTop: ${actualScrollTop} -> ${requestedScrollTop}`);
        this._ref.scrollTop = scrollTop;
        actualScrollTop = this.getScrollTop();
        if (this._debugMode && Math.abs(requestedScrollTop - actualScrollTop) > 0.5)
            console.warn(`${LogScope}.setScrollTop: post-set ${actualScrollTop} != ${requestedScrollTop}`);
        return actualScrollTop;
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private maybeOnRenderStart = (event: Event): void => {
        // if (this._debugMode)
        //     console.log(`${LogScope}.maybeOnRenderStart:`, event);
        const rsDataRef = this.getRenderStateDataRef();
        if (rsDataRef == null)
            return;
        const rsJson = rsDataRef.textContent;
        if (rsJson == null || rsJson === '')
            return;

        const rs = JSON.parse(rsJson) as Required<IRenderState>;
        rs.hasUnmeasuredItems = rs.scrollHeight == null;

        if (rs.renderIndex <= this._renderState.renderIndex)
            return;
        if (this._nextRenderState != null && rs.renderIndex <= this._nextRenderState.renderIndex)
            return;
        this.onRenderStart(rs);
    };

    private onRenderStart(rs: Required<IRenderState>) {
        this._isRendering = true;
        this._nextRenderState = rs;
        if (this._debugMode)
            console.log(`${LogScope}.onRenderStart, renderIndex = #${rs.renderIndex}, renderState = ${JSON.stringify(rs)}`);

        this._scrollTopPivotRef = null;
        this._scrollTopPivotOffset = null;
        this._renderStartScrollTop = this.getScrollTop();
        if (!rs.hasUnmeasuredItems)
            return;

        const itemY0 = this.getItemY0();
        const scrollHeight = this._ref.scrollHeight - this._spacerRef.clientHeight - this._endSpacerRef.clientHeight;
        const viewportHeight = this._ref.clientHeight;
        const scrollTop = this._renderStartScrollTop;
        const scrollBottom = scrollTop + viewportHeight;

        if (scrollTop < EdgeEpsilon) {
            // Spacer overlaps with the top of the viewport
            const itemRefs = this.getOrderedItemRefs();
            for (const item of itemRefs) {
                const key = item.dataset['key'];
                const knownSize = rs.itemSizes[key];
                if (!(knownSize > 0))
                    continue; // Item won't exist once rendering completes
                const y = item.getBoundingClientRect().y - itemY0;
                if (y >= scrollTop) {
                    this._scrollTopPivotRef = item;
                    this._scrollTopPivotOffset = y - scrollTop;
                    break;
                }
            }
        }
        else if (scrollHeight < scrollBottom + EdgeEpsilon) {
            // End spacer overlaps with the bottom of the viewport
            const itemRefs = this.getOrderedItemRefs(true);
            for (const item of itemRefs) {
                const key = item.dataset['key'];
                const knownSize = rs.itemSizes[key];
                if (!(knownSize > 0))
                    continue; // Item won't exist once rendering completes
                const y = item.getBoundingClientRect().y - itemY0;
                if (y <= scrollBottom) {
                    this._scrollTopPivotRef = item;
                    this._scrollTopPivotOffset = y - scrollTop;
                    break;
                }
            }
        }
        if (this._debugMode && this._scrollTopPivotRef != null)
            console.warn(`${LogScope}.onRenderStart: scrollTopPivot (top) = ` +
                `${JSON.stringify(this._scrollTopPivotRef)}, ` +
                `${JSON.stringify(this._scrollTopPivotOffset)}`);
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private maybeOnRenderEnd = (mutations: MutationRecord[], observer: MutationObserver): void => {
        // if (this._debugMode)
        //     console.log(`${LogScope}.maybeOnRenderEnd:`, mutations);
        const riText = this._renderIndexRef.dataset['renderIndex'];
        if (riText == null || riText == '')
            return;
        const ri = Number.parseInt(riText);
        if (ri != this._nextRenderState.renderIndex)
            return;
        void this.onRenderEnd();
    };

    private async onRenderEnd(): Promise<void> {
        const rs = this._renderState = this._nextRenderState;
        if (this._debugMode)
            console.log(`${LogScope}.onRenderEnd, renderIndex = #${rs.renderIndex}`);

        const itemY0 = this.getItemY0();
        const scrollHeight = this._ref.scrollHeight - this._spacerRef.clientHeight - this._endSpacerRef.clientHeight;
        const viewportHeight = this._ref.clientHeight;
        let scrollTop = this.getScrollTop();

        if (this._isFirstRender) {
            this._isFirstRender = false;
            rs.scrollTop = this._isEndAligned ? scrollHeight - viewportHeight : 0;
            rs.mustScroll = true;
        }

        if (rs.mustScroll && Math.abs(rs.scrollTop - scrollTop) > SizeEpsilon) {
            if (this._debugMode)
                console.warn(`${LogScope}.onRenderEnd: server-side scroll request`);
            scrollTop = this.setScrollTop(rs.scrollTop);
        }
        else if (this._scrollTopPivotRef != null) {
            const newScrollTopPivotOffset = this._scrollTopPivotRef.getBoundingClientRect().y - itemY0 - scrollTop;
            const dScrollTop = newScrollTopPivotOffset - this._scrollTopPivotOffset;
            const newScrollTop = scrollTop + dScrollTop;
            if (this._debugMode)
                console.warn(`${LogScope}.onRenderEnd: resync scrollTop: ${scrollTop} + ${dScrollTop} -> ${newScrollTop}`);
            if (Math.abs(dScrollTop) > SizeEpsilon)
                scrollTop = this.setScrollTop(newScrollTop);
        }

        this._renderEndScrollTop = scrollTop;
        this._isRendering = false;
        this.resetResizeTracking();

        if (rs.renderIndex < this._updateClientSideStateRenderIndex) {
            // This is an outdated update already
            if (this._debugMode)
                console.log(`${LogScope}.onRenderEnd skips updateClientSideStateDebounced:` +
                    ` #${rs.renderIndex} < #${this._updateClientSideStateRenderIndex}`);
            return; // such an update will be ignored anyway
        }

        if (rs.scrollTop != null)
            await delayAsync(RenderToUpdateClientSideStateDelay);
        this.updateClientSideStateDebounced(true);
    }

    protected updateClientSideStateDebounced(immediately = false) {
        if (this._debugMode)
            console.log(`${LogScope}.updateClientSideStateDebounced: immediately = ${immediately.toString()}`);
        if (immediately) {
            if (this._updateClientSideStateTimeout != null) {
                clearTimeout(this._updateClientSideStateTimeout);
                this._updateClientSideStateTimeout = null;
            }
            void this.updateClientSideState();
        } else {
            if (this._updateClientSideStateTimeout != null)
                return;
            this._updateClientSideStateTimeout = self.setTimeout(() => {
                this._updateClientSideStateTimeout = null;
                void this.updateClientSideState();
            }, UpdateClientSideStateTimeout);
        }
    }

    protected async updateClientSideState(): Promise<void> {
        const queue = this._updateClientSideStateTasks;
        const lastTask = queue.length > 0 ? queue[queue.length - 1] : null;
        if (queue.length >= 2)
            return lastTask;
        const newTask = (async () => {
            try {
                if (lastTask != null)
                    await lastTask.then(v => v, _ => null);
                await this.updateClientSideStateImpl();
            }
            finally {
                void queue.shift();
            }
        })();
        queue.push(newTask);
    }

    protected async updateClientSideStateImpl(): Promise<void> {
        while (this._isRendering)
            await nextTickAsync();

        const rs = this._renderState;
        if (rs.renderIndex < this._updateClientSideStateRenderIndex) {
            // This update will be dropped by server
            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideStateImpl: skipped for` +
                    ` #${rs.renderIndex} < #${this._updateClientSideStateRenderIndex}`);
            return; // This update was already pushed
        }

        if (this._debugMode)
            console.log(`${LogScope}.updateClientSideStateImpl: #${rs.renderIndex}`);

        const spacerRect = this._spacerRef.getBoundingClientRect();
        const endSpacerRect = this._endSpacerRef.getBoundingClientRect();

        const spacerSize = spacerRect.height;
        const endSpacerSize = endSpacerRect.height;
        const scrollHeight = this._ref.scrollHeight;
        const viewportHeight = this._ref.clientHeight;
        const scrollTop = this.getScrollTop();

        const state: Required<IClientSideState> = {
            renderIndex: rs.renderIndex,

            spacerSize: spacerSize,
            endSpacerSize: endSpacerSize,
            scrollHeight: scrollHeight,
            scrollTop: scrollTop,
            viewportHeight: viewportHeight,

            itemSizes: {}, // Will be updated further

            isSafeToScroll: this._isSafeToScroll,
            isListResized: this._listResizeEventCount > 1, // First one is always the initial measurement event
            isViewportChanged: false, // Will be updated further
            isUserScrollDetected: false, // Will be updated further
        };

        let gotResizedItems = false;
        for (const item of this.getItemRefs()) {
            const key = item.dataset['key'];
            const knownSize = rs.itemSizes[key];
            const size = item.getBoundingClientRect().height;
            if (knownSize < 0) {
                state.itemSizes[key] = size;
            }
            else if (Math.abs(size - knownSize) > ItemSizeEpsilon) {
                state.itemSizes[key] = size;
                gotResizedItems = true;
            }
        }

        const hasViewport = rs.viewportHeight != null;
        const isKnownScrollTopChanged = state.scrollTop == null || Math.abs(state.scrollTop - rs.scrollTop) > MoveSizeEpsilon;
        const isScrollHappened = Math.abs(state.scrollTop - this._renderEndScrollTop) > MoveSizeEpsilon;
        const isScrollHeightChanged = rs.hasUnmeasuredItems || Math.abs(state.scrollHeight - rs.scrollHeight) > MoveSizeEpsilon;
        const isViewportHeightChanged = !hasViewport || Math.abs(state.viewportHeight - rs.viewportHeight) > MoveSizeEpsilon;
        state.isViewportChanged = isKnownScrollTopChanged || isScrollHeightChanged || isViewportHeightChanged;
        state.isUserScrollDetected = isScrollHappened && !(state.isListResized || gotResizedItems);
        if (this._debugMode) {
            console.log(`${LogScope}.updateClientSideStateImpl: changes:` +
                (Object.keys(state.itemSizes).length > 0 ? ' [items sizes]' : '') +
                (state.isUserScrollDetected ? ' [user scroll]' : '') +
                (state.isViewportChanged ? ' [viewport]' : '') +
                (state.isListResized ? ' [body resized]' : ''));
            if (state.isViewportChanged)
                console.log(`${LogScope}.updateClientSideStateImpl: viewport change:` +
                    (isKnownScrollTopChanged ? ` [scrollTop: ${rs.scrollTop} -> ${state.scrollTop}]` : '') +
                    (isScrollHeightChanged ? ` [scrollHeight: ${rs.scrollHeight} -> ${state.scrollHeight}]` : '') +
                    (isViewportHeightChanged ? ` [viewportHeight: ${rs.viewportHeight} -> ${state.viewportHeight}]` : ''));
        }

        const mustUpdateClientSideState =
            state.isViewportChanged
            || Object.keys(state.itemSizes).length > 0
            || (rs.notifyWhenSafeToScroll && state.isSafeToScroll);
        if (!mustUpdateClientSideState) {
            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideStateImpl: server call skipped`);
            return;
        }

        if (this._debugMode)
            console.log(`${LogScope}.updateClientSideStateImpl: server call, state = ${JSON.stringify(state)}`);
        const result: number = await this._blazorRef.invokeMethodAsync('UpdateClientSideState', state);
        if (result > this._updateClientSideStateRenderIndex)
            this._updateClientSideStateRenderIndex = result;
    }

    private resetResizeTracking() {
        this._listResizeEventCount = 0;

        this._resizeObserver.disconnect();
        this._resizeObserver.observe(this._ref);
        for (const item of this.getItemRefs())
            this._resizeObserver.observe(item);
    }

    private onScroll = (): void => {
        if (this._isRendering)
            return;
        this._isSafeToScroll = false;
        if (this._onScrollStoppedTimeout != null)
            clearTimeout(this._onScrollStoppedTimeout);
        this._onScrollStoppedTimeout = self.setTimeout(() => {
            this._onScrollStoppedTimeout = null;
            this._isSafeToScroll = true;
            if (this._renderState.notifyWhenSafeToScroll)
                this.updateClientSideStateDebounced(true);
        }, ScrollStoppedTimeout);

        this.updateClientSideStateDebounced();
    };

    private onResize(entries: ResizeObserverEntry[]) {
        if (this._isRendering)
            return;
        for (const entry of entries) {
            if (entry.target == this._ref)
                this._listResizeEventCount++;
        }
        this.updateClientSideStateDebounced();
    }
}

/** same as VirtualListClientSideState */
interface IClientSideState {
    renderIndex: number;

    spacerSize: number;
    endSpacerSize: number;
    scrollHeight: number;
    scrollTop: number;
    viewportHeight: number;

    itemSizes: Record<string, number>;

    isSafeToScroll: boolean;
    isListResized: boolean;
    isViewportChanged: boolean;
    isUserScrollDetected: boolean;
}

/** same as VirtualListRenderInfo */
interface IRenderState {
    renderIndex: number;

    spacerSize: number;
    endSpacerSize: number;
    scrollHeight?: number;
    scrollTop?: number;
    viewportHeight?: number;

    itemSizes: Record<string, number>;

    mustScroll: boolean;
    notifyWhenSafeToScroll: boolean;
    hasUnmeasuredItems?: boolean;
}

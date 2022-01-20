import './virtual-list.css';
import { delayAsync } from 'delay';
import { nextTickAsync } from 'next-tick';

const LogScope: string = 'VirtualList'
const ScrollStoppedTimeout: number = 2000;
const UpdateClientSideStateTimeout: number = 10;
const RenderToUpdateClientSideStateDelay: number = 20;
const SizeEpsilon: number = 1;
const ItemSizeEpsilon: number = 2;
const MoveSizeEpsilon: number = 16;
const StickyEdgeEpsilon: number = 8;

export class VirtualList {
    private readonly _debugMode: boolean = false;
    private readonly _isEndAligned: boolean = false;
    /** ref to div.virtual-list */
    private readonly _elementRef: HTMLElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _displayedItemsRef: HTMLElement;
    private readonly _renderIndexRef: HTMLElement;
    private readonly _renderStateRef: HTMLElement;
    private readonly _abortController: AbortController;
    private readonly _resizeObserver: ResizeObserver;
    private readonly _renderIndexObserver: MutationObserver;
    private readonly _renderStateObserver: MutationObserver;
    private _listResizeEventCount: number = 0;
    private _isRendering: boolean = false;
    private _preRenderScrollTop: number = 0;
    private _postRenderScrollTop: number = 0;

    private _renderState: Required<IRenderState>
    private _nextRenderState?: Required<IRenderState> = null;
    private _isFirstRender: boolean = true;
    private _isSafeToScroll: boolean = true;
    private _onScrollStoppedTimeout: any = null;

    private _updateClientSideStateTimeout: any = null;
    private _updateClientSideStateTasks: Promise<unknown>[] = [];
    private _updateClientSideStateRenderIndex: number = -1;

    public static create(
        elementRef: HTMLElement, backendRef: DotNet.DotNetObject,
        isEndAligned: boolean, debugMode: boolean)
    {
        return new VirtualList(elementRef, backendRef, isEndAligned, debugMode);
    }

    public constructor(
        elementRef: HTMLElement, backendRef: DotNet.DotNetObject,
        isEndAligned: boolean, debugMode: boolean)
    {
        if (debugMode) {
            console.log(`${LogScope}.ctor`);
            window["virtualList"] = this;
        }

        this._debugMode = debugMode;
        this._isEndAligned = isEndAligned;
        this._elementRef = elementRef;
        this._blazorRef = backendRef;
        this._abortController = new AbortController();
        this._displayedItemsRef = this._elementRef.querySelector(".items-displayed");
        this._renderIndexRef = this._elementRef.querySelector(".data.render-index")!;
        this._renderStateRef = this._elementRef.querySelector(".data.render-state")!;
        this._resizeObserver = new ResizeObserver(entries => this.onResize(entries));
        this._renderStateObserver = new MutationObserver(_ => this.onRenderStart());
        this._renderStateObserver.observe(this._renderStateRef, { attributes: true, attributeFilter: ["data-render-state"] })
        this._renderIndexObserver = new MutationObserver(_ => this.onRenderEnd());
        this._renderIndexObserver.observe(this._renderIndexRef, { attributes: true, attributeFilter: ["data-render-index"] })
        const listenerOptions: any = { signal: this._abortController.signal };
        this._elementRef.addEventListener("scroll", _ => this.onScroll(), listenerOptions);

        this._renderState = {
            renderIndex: -1,

            spacerSize: 0,
            endSpacerSize: 0,
            scrollHeight: 0,
            scrollTop: null,
            viewportHeight: null,

            itemSizes: {},

            mustMeasure: false,
            mustScroll: false,
            notifyWhenSafeToScroll: false
        };

        this.onRenderStart();
        const _ = this.onRenderEnd();
    };

    public dispose() {
        this._abortController.abort();
        this._resizeObserver.disconnect();
        this._renderIndexObserver.disconnect();
        this._renderStateObserver.disconnect();
    }

    private getSpacerRef() : HTMLElement {
        return this._elementRef.querySelector(".spacer-start");
    }

    private getEndSpacerRef() : HTMLElement {
        return this._elementRef.querySelector(".spacer-end");
    }

    private onRenderStart() {
        this._isRendering = true;
        this._preRenderScrollTop = this._elementRef.scrollTop;
        const rsJson = this._renderStateRef.dataset["renderState"];
        if (rsJson == null || rsJson === "")
            return;
        const rs = this._renderState = JSON.parse(rsJson);
        if (this._debugMode)
            console.log(`${LogScope}.onRenderStart, renderIndex = #${rs.renderIndex}, renderState =`, rs);
        this._nextRenderState = rs;
    }

    private async onRenderEnd() : Promise<void> {
        if (this._debugMode)
            console.log(`${LogScope}.onRenderEnd`);
        const rs = this._nextRenderState;
        const spacerSize = this.getSpacerRef().getBoundingClientRect().height;
        const endSpacerSize = this.getEndSpacerRef().getBoundingClientRect().height;
        const displayedItemsSize = this._displayedItemsRef.getBoundingClientRect().height;
        const scrollHeight = this._elementRef.scrollHeight;
        const computedScrollHeight = spacerSize + endSpacerSize + displayedItemsSize;
        const viewportHeight = this._elementRef.getBoundingClientRect().height;
        let scrollTop = this._elementRef.scrollTop;
        if (this._isFirstRender) {
            this._isFirstRender = false;
            rs.scrollTop = this._isEndAligned ? -rs.endSpacerSize : rs.spacerSize;
            rs.mustScroll = true;
        }
        if (rs.mustScroll && Math.abs(rs.scrollTop - scrollTop) > SizeEpsilon) {
            if (this._debugMode)
                console.warn(`${LogScope}.onRenderEnd: scrollTop: ${scrollTop} -> ${rs.scrollTop}`)
            scrollTop = this._elementRef.scrollTop = rs.scrollTop;
        }
        this._postRenderScrollTop = scrollTop;
        this._isRendering = false;
        this.resetResizeTracking();

        if (this._debugMode) {
            // Render consistency checks
            let reportDetails = false;
            if (rs.scrollTop != null && Math.abs(rs.scrollTop - scrollTop) > MoveSizeEpsilon) {
                console.warn(`${LogScope}.onRenderEnd: scrollTop mismatch: actual ${scrollTop} != ${rs.scrollTop}`);
                reportDetails = true;
            }
            if (Math.abs(rs.scrollHeight - scrollHeight) > SizeEpsilon) {
                console.warn(`${LogScope}.onRenderEnd: scrollHeight mismatch: actual ${scrollHeight} != ${rs.scrollHeight}`);
                reportDetails = true;
            }
            if (Math.abs(rs.scrollHeight - computedScrollHeight) > SizeEpsilon) {
                console.warn(`${LogScope}.onRenderEnd: computedScrollHeight mismatch: actual ${scrollHeight} != ${rs.scrollHeight}`);
                reportDetails = true;
            }
            if (reportDetails) {
                if (Math.abs(rs.spacerSize - spacerSize) > SizeEpsilon)
                    console.warn(`! spacerSize: actual ${spacerSize} != ${rs.spacerSize}`);
                if (Math.abs(rs.endSpacerSize - endSpacerSize) > SizeEpsilon)
                    console.warn(`! endSpacerSize: actual ${endSpacerSize} != ${rs.endSpacerSize}`);
                const items = this._elementRef.querySelectorAll(".items-displayed > .item").values() as IterableIterator<HTMLElement>;
                for (const item of items) {
                    const key = item.dataset["key"];
                    const knownSize = rs.itemSizes[key];
                    const size = item.getBoundingClientRect().height;
                    if (Math.abs(size - knownSize) > SizeEpsilon)
                        console.warn(`! item key = ${key} size: actual ${size} != ${knownSize}`);
                }
            }
        }

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

    protected updateClientSideStateDebounced(immediately: boolean = false)
    {
        if (this._debugMode)
            console.log(`${LogScope}.updateClientSideStateDebounced: immediately = ${immediately}`);
        if (immediately) {
            if (this._updateClientSideStateTimeout != null) {
                clearTimeout(this._updateClientSideStateTimeout);
                this._updateClientSideStateTimeout = null;
            }
            const _ = this.updateClientSideState();
        } else {
            if (this._updateClientSideStateTimeout != null)
                return;
            this._updateClientSideStateTimeout =
                setTimeout(() => {
                    this._updateClientSideStateTimeout = null;
                    const _ = this.updateClientSideState();
                }, UpdateClientSideStateTimeout)
        }
    }

    protected updateClientSideState() {
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
                const _ = queue.shift();
            }
        })();
        queue.push(newTask)
    }

    protected async updateClientSideStateImpl() : Promise<void> {
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

        const spacerSize = this.getSpacerRef().getBoundingClientRect().height;
        const endSpacerSize = this.getEndSpacerRef().getBoundingClientRect().height;
        const displayedItemsSize = this._displayedItemsRef.getBoundingClientRect().height;
        const scrollHeight = this._elementRef.scrollHeight;
        const computedScrollHeight = spacerSize + endSpacerSize + displayedItemsSize;
        const viewportHeight = this._elementRef.getBoundingClientRect().height;
        const scrollTop = this._elementRef.scrollTop;

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

        let gotNewlyMeasuredItems = false;
        if (rs.mustMeasure) {
            const items = this._elementRef.querySelectorAll(".items-unmeasured > .item").values() as IterableIterator<HTMLElement>;
            for (const item of items) {
                const key = item.dataset["key"];
                state.itemSizes[key] = item.getBoundingClientRect().height;
                gotNewlyMeasuredItems = true;
            }
            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideStateImpl: measured items: `, state.itemSizes);
        }

        let gotResizedItems = false;
        const items = this._elementRef.querySelectorAll(".items-displayed > .item").values() as IterableIterator<HTMLElement>;
        for (const item of items) {
            const key = item.dataset["key"];
            const knownSize = rs.itemSizes[key];
            const size = item.getBoundingClientRect().height;
            if (Math.abs(size - knownSize) > ItemSizeEpsilon) {
                state.itemSizes[key] = size;
                gotResizedItems = true;
            }
        }

        const isScrollTopChanged = Math.abs(state.scrollTop - rs.scrollTop) > MoveSizeEpsilon;
        const isPostRenderScrollTopChanged = Math.abs(state.scrollTop - this._postRenderScrollTop) > MoveSizeEpsilon;
        const isScrollHeightChanged = Math.abs(state.scrollHeight - rs.scrollHeight) > MoveSizeEpsilon;
        const isViewportHeightChanged = rs.viewportHeight == null
            || Math.abs(state.viewportHeight - rs.viewportHeight) > MoveSizeEpsilon;
        state.isViewportChanged = isScrollTopChanged || isScrollHeightChanged || isViewportHeightChanged;
        state.isUserScrollDetected = isPostRenderScrollTopChanged && !(state.isListResized || gotResizedItems);
        if (this._debugMode) {
            console.log(`${LogScope}.updateClientSideStateImpl: changes:` +
                (Object.keys(state.itemSizes).length > 0 ? " [items sizes]" : "") +
                (state.isUserScrollDetected ? " [user scroll]" : "") +
                (state.isViewportChanged ? " [viewport]" : "") +
                (state.isListResized ? " [body resized]" : ""));
            if (state.isViewportChanged)
                console.log(`${LogScope}.updateClientSideStateImpl: viewport change:` +
                    (isScrollTopChanged ? ` [scrollTop: ${rs.scrollTop} -> ${state.scrollTop}]` : "") +
                    (isScrollHeightChanged ? ` [scrollHeight: ${rs.scrollHeight} -> ${state.scrollHeight}]` : "") +
                    (isViewportHeightChanged ? ` [viewportHeight: ${rs.viewportHeight} -> ${state.viewportHeight}]` : ""));
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
            console.log(`${LogScope}.updateClientSideStateImpl: server call, state = `, state);
        const result : number = await this._blazorRef.invokeMethodAsync("UpdateClientSideState", state)
        if (result > this._updateClientSideStateRenderIndex)
            this._updateClientSideStateRenderIndex = result;
    }

    private resetResizeTracking() {
        this._listResizeEventCount = 0;

        this._resizeObserver.disconnect();
        this._resizeObserver.observe(this._elementRef);
        const items = this._elementRef.querySelectorAll(".items-displayed > .item").values();
        for (const item of items)
            this._resizeObserver.observe(item);
    }

    private onScroll() {
        if (this._isRendering)
            return;
        this._isSafeToScroll = false;
        if (this._onScrollStoppedTimeout != null)
            clearTimeout(this._onScrollStoppedTimeout);
        this._onScrollStoppedTimeout =
            setTimeout(() => {
                this._onScrollStoppedTimeout = null;
                this._isSafeToScroll = true;
                if (this._renderState.notifyWhenSafeToScroll)
                    this.updateClientSideStateDebounced(true);
            }, ScrollStoppedTimeout);

        this.updateClientSideStateDebounced();
    }

    private onResize(entries: ResizeObserverEntry[]) {
        if (this._isRendering)
            return;
        for (const entry of entries) {
            if (entry.target == this._elementRef)
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
    scrollHeight: number;
    scrollTop?: number;
    viewportHeight?: number;

    itemSizes: Record<string, number>;

    mustMeasure: boolean
    mustScroll: boolean
    notifyWhenSafeToScroll: boolean
}

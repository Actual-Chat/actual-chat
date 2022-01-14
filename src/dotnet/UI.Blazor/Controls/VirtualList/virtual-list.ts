import './virtual-list.css';

const LogScope: string = 'VirtualList'
const ScrollStoppedTimeout: number = 2000;
const UpdateClientSideStateTimeout: number = 200;
const AfterRenderRenderWaitTimeout: number = 5;
const AfterRenderUpdateClientSideStateTimeout: number = 20;
const DebugModeUpdateClientSideStateTimeout: number = 2000;
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
    private readonly _spacerRef: HTMLElement;
    private readonly _endSpacerRef: HTMLElement;
    private readonly _displayedItemsRef: HTMLElement;
    private readonly _renderStateRef: HTMLElement;
    private readonly _abortController: AbortController;
    private readonly _resizeObserver: ResizeObserver;
    private readonly _mutationObserver: MutationObserver;
    private _listResizeEventCount: number = 0;

    private _renderState: Required<IRenderState>
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
        this._debugMode = debugMode;
        this._isEndAligned = isEndAligned;
        this._elementRef = elementRef;
        this._blazorRef = backendRef;
        this._abortController = new AbortController();
        this._spacerRef = this._elementRef.querySelector(".spacer-start")!;
        this._endSpacerRef = this._elementRef.querySelector(".spacer-end")!;
        this._displayedItemsRef = this._elementRef.querySelector(".items-displayed");
        this._renderStateRef = this._elementRef.querySelector(".render-state")!;
        this._resizeObserver = new ResizeObserver(entries => this.onResize(entries));
        this._mutationObserver = new MutationObserver(_ => this.onRendered());
        this._mutationObserver.observe(this._renderStateRef, { attributes: true, attributeFilter: ["data-render-state"] })
        let listenerOptions: any = { signal: this._abortController.signal };
        this._elementRef.addEventListener("scroll", _ => this.onScroll(), listenerOptions);

        this._renderState = {
            renderIndex: -1,

            spacerSize: 0,
            endSpacerSize: 0,
            scrollHeight: 0,
            itemSizes: {},

            scrollTop: 0,
            clientHeight: 0,

            mustMeasure: false,
            mustScroll: false,
            notifyWhenSafeToScroll: false
        };
        if (debugMode)
            console.log(`${LogScope}.ctor`);
        this.onRendered();
    };

    public dispose() {
        this._abortController.abort();
        this._resizeObserver.disconnect();
        this._mutationObserver.disconnect();
    }

    protected isFullyRendered() {
        let renderIndex = parseInt(this._elementRef.dataset["renderIndex"]!);
        return renderIndex == this._renderState.renderIndex;
    }

    public onRendered() {
        const renderStateJson = this._renderStateRef.dataset["renderState"];
        if (renderStateJson == null || renderStateJson === "")
            return;
        const renderState = this._renderState = JSON.parse(renderStateJson);
        if (this._debugMode)
            console.log(`${LogScope}.onRendered, renderIndex = #${renderState.renderIndex}, renderState = `, renderState);

        const spacerSize = this._spacerRef.getBoundingClientRect().height;
        const endSpacerSize = this._endSpacerRef.getBoundingClientRect().height;
        const displayedItemsSize = this._displayedItemsRef.getBoundingClientRect().height;
        const scrollHeight = this._elementRef.scrollHeight;
        const clientHeight = this._elementRef.getBoundingClientRect().height;
        const computedScrollHeight = spacerSize + endSpacerSize + displayedItemsSize;
        let scrollTop = this._elementRef.scrollTop;

        if (renderState.mustScroll && Math.abs(renderState.scrollTop - scrollTop) > SizeEpsilon) {
            if (this._debugMode)
                console.warn(`${LogScope}.onRendered: scrollTop: ${scrollTop} -> ${renderState.scrollTop}`)
            scrollTop = this._elementRef.scrollTop = renderState.scrollTop;
        }
        this.resetResizeTracking();

        if (this._debugMode) {
            // Render consistency checks
            let reportDetails = false;
            if (Math.abs(renderState.scrollTop - scrollTop) > MoveSizeEpsilon) {
                console.warn(`${LogScope}.onRendered: scrollTop mismatch: actual ${scrollTop} != ${renderState.scrollTop}`);
                reportDetails = true;
            }
            if (Math.abs(renderState.scrollHeight - scrollHeight) > SizeEpsilon) {
                console.warn(`${LogScope}.onRendered: scrollHeight mismatch: actual ${scrollHeight} != ${renderState.scrollHeight}`);
                reportDetails = true;
            }
            if (Math.abs(renderState.scrollHeight - computedScrollHeight) > SizeEpsilon) {
                console.warn(`${LogScope}.onRendered: computedScrollHeight mismatch: actual ${scrollHeight} != ${renderState.scrollHeight}`);
                reportDetails = true;
            }
            if (reportDetails) {
                if (Math.abs(renderState.spacerSize - spacerSize) > SizeEpsilon)
                    console.warn(`! spacerSize: actual ${spacerSize} != ${renderState.spacerSize}`);
                if (Math.abs(renderState.endSpacerSize - endSpacerSize) > SizeEpsilon)
                    console.warn(`! endSpacerSize: actual ${endSpacerSize} != ${renderState.endSpacerSize}`);
                let items = this._elementRef.querySelectorAll(".items-displayed .item").values() as IterableIterator<HTMLElement>;
                for (let item of items) {
                    let key = item.dataset["key"];
                    let knownSize = renderState.itemSizes[key];
                    let size = item.getBoundingClientRect().height;
                    if (Math.abs(size - knownSize) > SizeEpsilon)
                        console.warn(`! item key = ${key} size: actual ${size} != ${knownSize}`);
                }
            }
        }

        if (renderState.renderIndex < this._updateClientSideStateRenderIndex) {
            // This is an outdated update already
            if (this._debugMode)
                console.log(`${LogScope}.onRendered skips updateClientSideStateDebounced:` +
                    ` #${renderState.renderIndex} < #${this._updateClientSideStateRenderIndex}`);
            return; // such an update will be ignored anyway
        }

        setTimeout(() => {
            this.updateClientSideStateDebounced(true);
        }, AfterRenderUpdateClientSideStateTimeout);
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
            let _ = this.updateClientSideState();
        } else {
            if (this._updateClientSideStateTimeout != null)
                return;
            this._updateClientSideStateTimeout =
                setTimeout(() => {
                    this._updateClientSideStateTimeout = null;
                    let _ = this.updateClientSideState();
                }, this._debugMode ? DebugModeUpdateClientSideStateTimeout : UpdateClientSideStateTimeout)
        }
    }

    protected updateClientSideState() {
        let queue = this._updateClientSideStateTasks;
        let lastTask = queue.length > 0 ? queue[queue.length - 1] : null;
        if (queue.length >= 2)
            return lastTask;
        let newTask = (async () => {
            try {
                if (lastTask != null)
                    await lastTask.then(v => v, _ => null);
                await this.updateClientSideStateImpl();
            }
            finally {
                let _ = queue.shift();
            }
        })();
        queue.push(newTask)
    }

    protected async updateClientSideStateImpl() {
        let rs = this._renderState;
        if (rs.renderIndex < this._updateClientSideStateRenderIndex) {
            // This update will be dropped by server
            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideStateImpl: skipped for` +
                    ` #${rs.renderIndex} < #${this._updateClientSideStateRenderIndex}`);
            return; // This update was already pushed
        }

        if (this._debugMode)
            console.log(`${LogScope}.updateClientSideStateImpl: #${rs.renderIndex}`);

        let spacerSize = this._spacerRef.getBoundingClientRect().height;
        let endSpacerSize = this._endSpacerRef.getBoundingClientRect().height;
        let displayedItemsSize = this._displayedItemsRef.getBoundingClientRect().height;
        let scrollHeight = this._elementRef.scrollHeight;
        let clientHeight = this._elementRef.getBoundingClientRect().height;
        let computedScrollHeight = spacerSize + endSpacerSize + displayedItemsSize;
        let scrollTop = this._elementRef.scrollTop;

        let state: Required<IClientSideState> = {
            renderIndex: rs.renderIndex,

            spacerSize: spacerSize,
            endSpacerSize: endSpacerSize,
            scrollHeight: scrollHeight,
            itemSizes: {}, // Will be updated further

            scrollTop: scrollTop,
            clientHeight: clientHeight,

            isSafeToScroll: this._isSafeToScroll,
            isListResized: this._listResizeEventCount > 1, // First one is always the initial measurement event
            isViewportChanged: false, // Will be updated further
            isUserScrollDetected: false, // Will be updated further
        };

        let gotNewlyMeasuredItems = false;
        if (rs.mustMeasure) {
            let items = this._elementRef.querySelectorAll(".items-unmeasured .item").values() as IterableIterator<HTMLElement>;
            for (let item of items) {
                let key = item.dataset["key"];
                state.itemSizes[key] = item.getBoundingClientRect().height;
                gotNewlyMeasuredItems = true;
            }
            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideStateImpl: measured items: `, state.itemSizes);
        }

        let gotResizedItems = false;
        let items = this._elementRef.querySelectorAll(".items-displayed .item").values() as IterableIterator<HTMLElement>;
        for (let item of items) {
            let key = item.dataset["key"];
            let knownSize = rs.itemSizes[key];
            let size = item.getBoundingClientRect().height;
            if (Math.abs(size - knownSize) > ItemSizeEpsilon) {
                state.itemSizes[key] = size;
                gotResizedItems = true;
            }
        }

        let isScrollTopChanged = Math.abs(state.scrollTop - rs.scrollTop) > MoveSizeEpsilon;
        let isScrollHeightChanged = Math.abs(state.scrollHeight - rs.scrollHeight) > MoveSizeEpsilon;
        let isClientHeightChanged = Math.abs(state.clientHeight - rs.clientHeight) > MoveSizeEpsilon;
        state.isViewportChanged = isScrollTopChanged || isScrollHeightChanged || isClientHeightChanged;

        let wasAtTheEnd = Math.abs(rs.scrollTop + rs.clientHeight - rs.scrollHeight) <= StickyEdgeEpsilon;
        let isAtTheEnd = Math.abs(state.scrollTop + state.clientHeight - state.scrollHeight) <= StickyEdgeEpsilon;
        let stillAtTheEnd = wasAtTheEnd && isAtTheEnd;
        state.isUserScrollDetected = isScrollTopChanged && !stillAtTheEnd && !state.isListResized;
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
                    (isClientHeightChanged ? ` [clientHeight: ${rs.clientHeight} -> ${state.clientHeight}]` : ""));
            if (wasAtTheEnd != isAtTheEnd)
                console.log(`${LogScope}.updateClientSideStateImpl: location change:` +
                    (wasAtTheEnd ? " [was @ end," : " [wasn't @ end,") +
                    ` ${rs.scrollTop} + ${rs.clientHeight} == ${rs.scrollHeight}]` +
                    (isAtTheEnd ? " [is @ end," : " [isn't @ end,") +
                    ` ${state.scrollTop} + ${state.clientHeight} == ${state.scrollHeight}],`+
                    ` [computedScrollHeight = ${(computedScrollHeight)}]`);
        }

        let mustUpdateClientSideState =
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
        let result : number = await this._blazorRef.invokeMethodAsync("UpdateClientSideState", state)
        if (result > this._updateClientSideStateRenderIndex)
            this._updateClientSideStateRenderIndex = result;
    }

    /** setups resize notifications */
    private resetResizeTracking() {
        this._listResizeEventCount = 0;

        this._resizeObserver.disconnect();
        this._resizeObserver.observe(this._elementRef);
        let items = this._elementRef.querySelectorAll(".items-displayed .item").values();
        for (let item of items)
            this._resizeObserver.observe(item);
    }

    private onScroll() {
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
        if (!this.isFullyRendered())
            return; // We aren't interested in render/programmatic resize events

        for (let entry of entries) {
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
    itemSizes: Record<string, number>;

    scrollTop: number;
    clientHeight: number;

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
    scrollHeight: number
    itemSizes: Record<string, number>;

    scrollTop: number;
    clientHeight: number;

    mustMeasure: boolean
    mustScroll: boolean
    notifyWhenSafeToScroll: boolean
}

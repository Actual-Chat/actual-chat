import './virtual-list.css';

const LogScope: string = 'VirtualList'
const ScrollStoppedTimeout: number = 2000;
const UpdateClientSideStateTimeout: number = 200;
const SizeEpsilon: number = 0.6;
const StickyEdgeEpsilon: number = 4;

export class VirtualList {
    /** ref to div.virtual-list */
    private readonly _debugMode: boolean = false;
    private readonly _elementRef: HTMLElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _spacerRef: HTMLElement;
    private readonly _endSpacerRef: HTMLElement;
    private readonly _displayedItemsRef: HTMLElement;
    private readonly _abortController: AbortController;
    private readonly _resizeObserver: ResizeObserver;
    private _listResizeEventCount: number = 0;

    private _renderState: Required<IRenderState>
    private _isSafeToScroll: boolean = true;
    private _onScrollStoppedTimeout: any = null;

    private _updateClientSideStateTimeout: any = null;
    private _updateClientSideStateTask: Promise<unknown> | null = null;
    private _blazorRenderIndex: number = -1;

    public static create(elementRef: HTMLElement, backendRef: DotNet.DotNetObject, debugMode: boolean) {
        return new VirtualList(elementRef, backendRef, debugMode);
    }

    public constructor(elementRef: HTMLElement, backendRef: DotNet.DotNetObject, debugMode: boolean) {
        this._debugMode = debugMode;
        this._elementRef = elementRef;
        this._blazorRef = backendRef;
        this._abortController = new AbortController();
        this._spacerRef = this._elementRef.querySelector(".spacer-start")!;
        this._endSpacerRef = this._elementRef.querySelector(".spacer-end")!;
        this._displayedItemsRef = this._elementRef.querySelector(".items-displayed");
        this._resizeObserver = new ResizeObserver(entries => this.onResize(entries));
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
    };

    public dispose() {
        this._abortController.abort();
        this._resizeObserver.disconnect();
    }

    protected isFullyRendered() {
        let renderIndex = parseInt(this._elementRef.dataset["renderIndex"]!);
        return renderIndex == this._renderState.renderIndex;
    }

    public afterRender(renderState: Required<IRenderState>) {
        if (this._debugMode)
            console.log(`${LogScope}.afterRender, renderIndex = #${renderState.renderIndex}, renderState = `, renderState);
        if (renderState.mustScroll && Math.abs(renderState.scrollTop - this._elementRef.scrollTop) > SizeEpsilon) {
            if (this._debugMode)
                console.log(`${LogScope}.afterRender: scrolling to ${renderState.scrollTop}`)
            this._elementRef.scrollTop = renderState.scrollTop;
        }

        if (this._debugMode) {
            // Render consistency checks
            let spacerSize = this._spacerRef.getBoundingClientRect().height;
            let endSpacerSize = this._endSpacerRef.getBoundingClientRect().height;
            let displayedItemsSize = this._displayedItemsRef.getBoundingClientRect().height;
            let scrollHeight = spacerSize + endSpacerSize + displayedItemsSize;
            if (Math.abs(renderState.scrollHeight - scrollHeight) > SizeEpsilon) {
                console.warn(`${LogScope}.afterRender: scrollHeight doesn't match the expected one!`);
                if (Math.abs(renderState.spacerSize - spacerSize) > SizeEpsilon)
                    console.log(`! spacerSize: actual ${spacerSize} != ${renderState.spacerSize}`);
                if (Math.abs(renderState.endSpacerSize - endSpacerSize) > SizeEpsilon)
                    console.log(`! endSpacerSize: actual ${endSpacerSize} != ${renderState.endSpacerSize}`);
                let items = this._elementRef.querySelectorAll(".items-displayed .item").values() as IterableIterator<HTMLElement>;
                for (let item of items) {
                    let key = item.dataset["key"];
                    let knownSize = renderState.itemSizes[key];
                    let size = item.getBoundingClientRect().height;
                    if (Math.abs(size - knownSize) > SizeEpsilon)
                        console.log(`! item key = ${key} size: actual ${size} != ${knownSize}`);
                }
            }
        }

        this.resetResizeTracking();
        this._renderState = renderState; // At this point this.isFullyRendered() returns true

        if (renderState.renderIndex < this._blazorRenderIndex) {
            // This is an outdated update already
            if (this._debugMode)
                console.log(`${LogScope}.afterRender skips updateClientSideStateDebounced:` +
                    ` #${renderState.renderIndex} < #${this._blazorRenderIndex}`);
            return; // such an update will be ignored anyway
        }
        let isRenderIndexMatching = Math.abs(this._blazorRenderIndex - renderState.renderIndex) < 0.1;
        let immediately = renderState.mustMeasure || isRenderIndexMatching;
        this.updateClientSideStateDebounced(immediately);
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
                }, UpdateClientSideStateTimeout)
        }
    }

    protected updateClientSideState() {
        let prevTask = this._updateClientSideStateTask;
        let nextTask = (async () => {
            if (prevTask != null)
                await prevTask.then(v => v, _ => null);
            await this.updateClientSideStateImpl();
        })();
        this._updateClientSideStateTask = nextTask;
        return nextTask;
    }

    protected async updateClientSideStateImpl() {
        if (!this.isFullyRendered()) {
            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideStateImpl: skipped (not fully rendered)`);
            return; // Rendering is in progress, so the update will follow up anyway
        }

        let rs = this._renderState;
        if (rs.renderIndex < this._blazorRenderIndex) {
            // This update will be dropped by server
            if (this._debugMode)
                console.log(`${LogScope}.updateClientSideStateImpl: skipped for` +
                    ` #${rs.renderIndex} < #${this._blazorRenderIndex}`);
            return; // This update was already pushed
        }

        if (this._debugMode)
            console.log(`${LogScope}.updateClientSideStateImpl: #${rs.renderIndex}`);

        let spacerSize = this._spacerRef.getBoundingClientRect().height;
        let endSpacerSize = this._endSpacerRef.getBoundingClientRect().height;
        let displayedItemsSize = this._displayedItemsRef.getBoundingClientRect().height;
        let scrollHeight = spacerSize + endSpacerSize + displayedItemsSize;
        let trueScrollHeight = this._elementRef.scrollHeight;

        let state: Required<IClientSideState> = {
            renderIndex: rs.renderIndex,

            spacerSize: spacerSize,
            endSpacerSize: endSpacerSize,
            scrollHeight: scrollHeight,
            itemSizes: {}, // Will be updated further

            scrollTop: this._elementRef.scrollTop,
            clientHeight: this._elementRef.getBoundingClientRect().height,

            isSafeToScroll: this._isSafeToScroll,
            isListResized: this._listResizeEventCount > 1, // First is always an initial measure event
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
            if (Math.abs(size - knownSize) > SizeEpsilon) {
                state.itemSizes[key] = size;
                gotResizedItems = true;
            }
        }

        let isScrollTopChanged = Math.abs(state.scrollTop - rs.scrollTop) > SizeEpsilon;
        let isScrollHeightChanged = Math.abs(state.scrollHeight - rs.scrollHeight) > SizeEpsilon;
        let isClientHeightChanged = Math.abs(state.clientHeight - rs.clientHeight) > SizeEpsilon;
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
                    ` [trueScrollHeight = ${(trueScrollHeight)}]`);
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
        if (result > this._blazorRenderIndex)
            this._blazorRenderIndex = result;
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
        if (!this.isFullyRendered())
            return; // We aren't interested in render/programmatic scroll events

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

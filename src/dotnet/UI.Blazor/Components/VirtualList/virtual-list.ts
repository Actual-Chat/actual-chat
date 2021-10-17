import './virtual-list.css';

const ScrollStoppedTimeout: number = 2000;
const UpdateClientSideStateTimeout: number = 200;
const SizeEpsilon: number = 0.1

export class VirtualList {
    /** ref to div.virtual-list */
    private readonly _elementRef: HTMLElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _spacerRef: HTMLElement;
    private readonly _displayedItemsRef: HTMLElement;
    private readonly _abortController: AbortController;
    private readonly _resizeObserver: ResizeObserver;
    private _bodyResizeEventCount: number = 0;

    private _renderState: Required<IRenderState>
    private _isSafeToScroll: boolean = true;
    private _onScrollStoppedTimeout: any = null;

    private _updateClientSideStateTimeout: any = null;
    private _updateClientSideStateTask: Promise<unknown> | null = null;

    public static create(elementRef: HTMLElement, backendRef: DotNet.DotNetObject) {
        return new VirtualList(elementRef, backendRef);
    }

    public constructor(elementRef: HTMLElement, backendRef: DotNet.DotNetObject) {
        this._elementRef = elementRef;
        this._blazorRef = backendRef;
        this._abortController = new AbortController();
        this._spacerRef = this._elementRef.querySelector(".spacer")!;
        this._displayedItemsRef = this._elementRef.querySelector(".items-displayed");
        this._resizeObserver = new ResizeObserver(entries => this.onResize(entries));
        let listenerOptions: any = { signal: this._abortController.signal };
        this._elementRef.addEventListener("scroll", _ => this.onScroll(), listenerOptions);

        this._renderState = {
            renderIndex: -1,

            spacerSize: 0,
            scrollTop: 0,
            scrollHeight: 0,
            clientHeight: 0,
            itemSizes: {},

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
        console.log("afterRender: ", renderState);
        if (renderState.mustScroll && Math.abs(renderState.scrollTop - this._elementRef.scrollTop) > SizeEpsilon)
            this._elementRef.scrollTop = renderState.scrollTop;
        this.resetResizeTracking();
        this._renderState = renderState; // At this point this.isFullyRendered() returns true

        this.updateClientSideStateDebounced(renderState.mustMeasure);
    }

    protected updateClientSideStateDebounced(immediately: boolean = false)
    {
        if (immediately) {
            if (this._updateClientSideStateTimeout != null) {
                clearTimeout(this._updateClientSideStateTimeout);
                this._updateClientSideStateTimeout = null;
            }
            let _ = this.updateClientSideStateAsync();
        } else {
            if (this._updateClientSideStateTimeout != null)
                return;
            this._updateClientSideStateTimeout =
                setTimeout(() => {
                    this._updateClientSideStateTimeout = null;
                    let _ = this.updateClientSideStateAsync();
                }, UpdateClientSideStateTimeout)
        }
    }

    /** sends the state to UpdateClientSideState dotnet part */
    protected async updateClientSideStateAsync() {
        if (this._updateClientSideStateTask != null) {
            // this call should run in the same order / non-concurrently
            await this._updateClientSideStateTask.then(v => v, _ => null);
            this._updateClientSideStateTask = null;
        }
        if (!this.isFullyRendered())
            return; // Rendering is in progress, so the update will follow up anyway

        let rs = this._renderState;
        let state: Required<IClientSideState> = {
            renderIndex: rs.renderIndex,

            isSafeToScroll: this._isSafeToScroll,
            isBodyResized: this._bodyResizeEventCount > 1, // First is always an initial measure event
            isViewportChanged: false, // Will be updated further
            isUserScrollDetected: false, // Will be updated further

            spacerSize: rs.spacerSize,
            scrollTop: this._elementRef.scrollTop,
            scrollHeight: this._elementRef.scrollHeight,
            clientHeight: this._elementRef.getBoundingClientRect().height,
            itemSizes: {},
        };

        let gotNewlyMeasuredItems = false;
        if (rs.mustMeasure) {
            // console.log("Measuring new items...")
            let items = this._elementRef.querySelectorAll(".items-unmeasured .item").values() as IterableIterator<HTMLElement>;
            for (let item of items) {
                let key = item.dataset["key"];
                state.itemSizes[key] = item.getBoundingClientRect().height;
                gotNewlyMeasuredItems = true;
            }
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

        let wasBottomAligned = Math.abs(rs.scrollTop + rs.clientHeight - rs.scrollHeight) <= SizeEpsilon;
        let isBottomAligned = Math.abs(state.scrollTop + state.clientHeight - state.scrollHeight) <= SizeEpsilon;
        state.isUserScrollDetected =
            state.isBodyResized
            || isScrollTopChanged && !(wasBottomAligned && isBottomAligned);
        // console.log("isUserScrollDetected", state.isUserScrollDetected, wasBottomAligned, isBottomAligned, isScrollTopChanged);

        let mustUpdateClientSideState =
            state.isViewportChanged
            || Object.keys(state.itemSizes).length > 0
            || (rs.notifyWhenSafeToScroll && state.isSafeToScroll);
        if (!mustUpdateClientSideState)
            return;

        console.log("invoking UpdateClientSideState", state)
        this._updateClientSideStateTask = this._blazorRef.invokeMethodAsync("UpdateClientSideState", state);
    }

    /** setups resize notifications */
    private resetResizeTracking() {
        this._bodyResizeEventCount = 0;

        this._resizeObserver.disconnect();
        this._resizeObserver.observe(document.body);
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
                if (!this._renderState.notifyWhenSafeToScroll)
                    return
                this.updateClientSideStateDebounced();
            }, ScrollStoppedTimeout);

        this.updateClientSideStateDebounced();
    }

    private onResize(entries: ResizeObserverEntry[]) {
        if (!this.isFullyRendered())
            return; // We aren't interested in render/programmatic resize events

        for (let entry of entries) {
            if (entry.target == document.body)
                this._bodyResizeEventCount++;
        }
        this.updateClientSideStateDebounced();
    }
}

/** same as VirtualListClientSideState */
interface IClientSideState {
    renderIndex: number;

    isSafeToScroll: boolean;
    isBodyResized: boolean;
    isViewportChanged: boolean;
    isUserScrollDetected: boolean;

    spacerSize: number;
    scrollTop: number;
    scrollHeight: number
    clientHeight: number;
    itemSizes: Record<string, number>;
}

/** same as VirtualListRenderInfo */
interface IRenderState {
    renderIndex: number;

    spacerSize: number;
    scrollTop: number;
    scrollHeight: number
    clientHeight: number;
    itemSizes: Record<string, number>;

    mustMeasure: boolean
    mustScroll: boolean
    notifyWhenSafeToScroll: boolean
}

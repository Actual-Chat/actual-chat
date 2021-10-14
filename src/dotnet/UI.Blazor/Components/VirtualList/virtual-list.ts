import './virtual-list.css';

const ScrollNotifyTimeout : number = 100;
const ScrollStoppedTimeout : number = 2000;

export class VirtualList {
    /** ref to div.virtual-list */
    private readonly _elementRef: HTMLElement;
    private readonly _blazorRef: DotNet.DotNetObject;
    private readonly _spacerRef: HTMLElement;
    private readonly _displayedItemsRef: HTMLElement;
    private readonly _abortController: AbortController;

    private readonly _resizeObserver: ResizeObserver;
    private _onResizeTimeout: any = null;
    private _resizedOnce: Map<Element, boolean>;

    private _notifyWhenSafeToScroll: boolean = false;
    private _isSafeToScroll: boolean = true;
    private _onScrollNotifyTimeout: any = null;
    private _onScrollStoppedTimeout: any = null;

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
        this._resizedOnce = new Map<Element, boolean>();

        let listenerOptions: any = { signal: this._abortController.signal };
        this._elementRef.addEventListener("scroll", _ => this.onScroll(), listenerOptions);
    };

    public dispose() {
        this._abortController.abort();
        this._resizeObserver.disconnect();
    }

    public afterRender(mustScroll, scrollTop, notifyWhenSafeToScroll) {
        // console.log("afterRender: ", { mustScroll, scrollTop, notifyWhenSafeToScroll });
        this._notifyWhenSafeToScroll = notifyWhenSafeToScroll;
        if (mustScroll)
            this._elementRef.scrollTo(0, scrollTop);
        let _ = this.updateClientSideStateAsync();
        this.setupResizeTracking();
    }

    /** sends the state to UpdateClientSideState dotnet part */
    protected async updateClientSideStateAsync() {
        if (this._updateClientSideStateTask != null) {
            // this call should run in the same order / non-concurrently
            await this._updateClientSideStateTask.then(v => v, _ => null);
            this._updateClientSideStateTask = null;
        }
        let originalScrollTop = parseFloat(this._elementRef.dataset["scrollTop"]!);
        let originalClientHeight = parseFloat(this._elementRef.dataset["clientHeight"]!);
        let scrollTop = this._elementRef.scrollTop;
        let clientHeight = this._elementRef.getBoundingClientRect().height;
        let isUserScrollDetected =
            Math.abs(originalScrollTop - scrollTop) > 0.1
            || Math.abs(originalClientHeight - clientHeight) > 0.1;

        let state: Required<IClientSideState> = {
            RenderIndex: parseInt(this._elementRef.dataset["renderIndex"]!),
            IsSafeToScroll: this._isSafeToScroll,
            ScrollTop: scrollTop,
            ClientHeight: clientHeight,
            ItemSizes: {},
        };

        let items = this._elementRef.querySelectorAll(".items-unmeasured .item").values() as IterableIterator<HTMLElement>;
        for (let item of items) {
            let key = item.dataset["key"];
            state.ItemSizes[key] = item.getBoundingClientRect().height;
        }
        items = this._elementRef.querySelectorAll(".items-displayed .item").values() as IterableIterator<HTMLElement>;
        for (let item of items) {
            let key = item.dataset["key"];
            let knownSize = parseFloat(item.dataset["size"]!);
            let size = item.getBoundingClientRect().height;
            if (Math.abs(size - knownSize) >= 0.001)
                state.ItemSizes[key] = size;
        }

        let mustUpdateClientSideState =
            isUserScrollDetected
            || Object.keys(state.ItemSizes).length > 0
            || (this._notifyWhenSafeToScroll && state.IsSafeToScroll);
        if (!mustUpdateClientSideState)
            return;

        this._updateClientSideStateTask = this._blazorRef.invokeMethodAsync("UpdateClientSideState", state);
    }

    /** setups resize notifications */
    private setupResizeTracking() {
        this._resizeObserver.disconnect();
        this._resizedOnce = new Map<Element, boolean>();
        if (this._onResizeTimeout != null)
            clearTimeout(this._onResizeTimeout);
        let items = this._elementRef.querySelectorAll(".items-displayed .item").values();
        this._resizeObserver.observe(this._elementRef);
        for (let item of items)
            this._resizeObserver.observe(item);
    }

    private onScroll() {
        this._isSafeToScroll = false;
        if (this._onScrollNotifyTimeout == null)
            this._onScrollNotifyTimeout = setTimeout(
                () => this.onScrollNotify(), ScrollNotifyTimeout);
        if (this._onScrollStoppedTimeout != null)
            clearTimeout(this._onScrollStoppedTimeout);
        this._onScrollStoppedTimeout = setTimeout(
            () => this.onScrollStopped(), ScrollStoppedTimeout);
    }

    private onScrollNotify() {
        this._onScrollNotifyTimeout = null;
        let _ = this.updateClientSideStateAsync();
    }

    private onScrollStopped() {
        this._isSafeToScroll = true;
        if (!this._notifyWhenSafeToScroll)
            return
        let _ = this.updateClientSideStateAsync();
    }

    private onResize(entries: ResizeObserverEntry[]) {
        let mustIgnore = false;
        for (let entry of entries) {
            if (this._resizedOnce.has(entry.target))
                mustIgnore = true;
            this._resizedOnce.set(entry.target, true);
        }
        if (mustIgnore)
            return;

        if (this._onResizeTimeout != null)
            clearTimeout(this._onResizeTimeout);
        this._onResizeTimeout = setTimeout(() => this.updateClientSideStateAsync(), 50);
    }
}

/** should be in consist with IVirtualListBackend.ClientSideState */
interface IClientSideState {
    RenderIndex: number;

    /** Is Blazor side can call scroll programmly at the moment or not */
    IsSafeToScroll: boolean;

    /** Used to detect user scroll */
    ScrollTop: number;

    /** Height of div.virtual-list */
    ClientHeight: number;

    ItemSizes: Record<string, number>;
}

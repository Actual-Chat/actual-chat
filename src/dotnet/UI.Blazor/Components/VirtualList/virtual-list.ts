import './virtual-list.css'

export class VirtualList {
    private _elementRef: HTMLElement;
    private _blazorRef: DotNet.DotNetObject;
    private _spacerRef: HTMLElement;
    private _displayedItemsRef: HTMLElement;
    private _abortController: AbortController;

    private _resizeObserver: ResizeObserver;
    private _resizedOnce: Map<Element, boolean>;
    private _updateClientSideStateTask: Promise<unknown> | null;
    private _onScrollStoppedTimeout: any;
    private _onResizeTimeout: any;

    static create(elementRef: HTMLElement, backendRef: DotNet.DotNetObject) {
        return new VirtualList(elementRef, backendRef)
    }

    constructor(elementRef: HTMLElement, backendRef: DotNet.DotNetObject) {
        this._elementRef = elementRef;
        this._blazorRef = backendRef;
        this._abortController = new AbortController();
        this._spacerRef = this._elementRef.querySelector(".spacer")!;
        this._displayedItemsRef = this._elementRef.querySelector(".items-displayed") as HTMLElement;
        this._updateClientSideStateTask = null!;
        this._onScrollStoppedTimeout = null!;
        this._resizeObserver = new ResizeObserver(entries => this.onResize(entries));
        this._resizedOnce = new Map<Element, boolean>();

        let listenerOptions = { signal: this._abortController.signal };
        elementRef.addEventListener("scroll", _ => this.updateClientSideStateAsync(), listenerOptions);
    };

    dispose() {
        this._abortController.abort();
        this._resizeObserver.disconnect();
    }

    afterRender(mustScroll, viewOffset, mustNotifyWhenScrollStops) {
        let spacerSize = this.getSpacerSize();
        if (mustScroll)
            this._elementRef.scrollTo(0, viewOffset + spacerSize);
        let _ = this.updateClientSideStateAsync()
        this.setupResizeTracking();
        this.setupScrollTracking(mustNotifyWhenScrollStops);
    }

    // Scroll stopped notification

    setupScrollTracking(mustNotifyWhenScrollStops) {
        if (mustNotifyWhenScrollStops) {
            if (this._onScrollStoppedTimeout == null)
                this.onScroll();
        } else {
            if (this._onScrollStoppedTimeout != null)
                clearTimeout(this._onScrollStoppedTimeout);
            this._onScrollStoppedTimeout = null;
        }
    }

    onScroll() {
        if (this._onScrollStoppedTimeout != null)
            clearTimeout(this._onScrollStoppedTimeout);
        this._onScrollStoppedTimeout = setTimeout(
            () => this.updateClientSideStateAsync(true),
            2000);
    }

    // Resize notifications

    setupResizeTracking() {
        this._resizeObserver.disconnect();
        this._resizedOnce = new Map<Element, boolean>();
        if (this._onResizeTimeout != null)
            clearTimeout(this._onResizeTimeout);
        let items = this._elementRef.querySelectorAll(".items-displayed .item").values();
        this._resizeObserver.observe(this._elementRef);
        for (let item of items)
            this._resizeObserver.observe(item);
    }

    onResize(entries: ResizeObserverEntry[]) {
        let mustIgnore = false;
        for (let entry of entries) {
            if (this._resizedOnce.has(entry.target)) {
                mustIgnore = true;
            }
            this._resizedOnce.set(entry.target, true);
        }
        if (mustIgnore)
            return;

        if (this._onResizeTimeout != null)
            clearTimeout(this._onResizeTimeout);
        this._onResizeTimeout = setTimeout(
            () => this.updateClientSideStateAsync(),
            50);
    }

    // UpdateClientSideState caller

    async updateClientSideStateAsync(isScrollStopped = false) {
        if (this._updateClientSideStateTask != null) {
            // This call should run in the same order / non-concurrently
            await this._updateClientSideStateTask.then(v => v, _ => null);
            this._updateClientSideStateTask = null;
        }
        let spacerSize = this.getSpacerSize();
        let state = {
            renderIndex: parseInt(this._elementRef.dataset["renderIndex"]!),
            isScrollStopped: isScrollStopped,
            viewOffset: this._elementRef.scrollTop - spacerSize,
            viewSize: this._elementRef.getBoundingClientRect().height,
            itemSizes: {}
        };
        if (!isScrollStopped) {
            let items = this._elementRef.querySelectorAll(".items-unmeasured .item").values() as IterableIterator<HTMLElement>;
            for (let item of items) {
                let key = item.dataset["key"];
                state.itemSizes[key] = item.getBoundingClientRect().height;
            }
            items = this._elementRef.querySelectorAll(".items-displayed .item").values() as IterableIterator<HTMLElement>;
            for (let item of items) {
                let key = item.dataset["key"];
                let knownSize = parseFloat(item.dataset["size"]!)
                let size = item.getBoundingClientRect().height;
                if (Math.abs(size - knownSize) >= 0.001)
                    state.itemSizes[key] = size;
            }
        }
        this._updateClientSideStateTask = this._blazorRef.invokeMethodAsync("UpdateClientSideState", state);
    }

    // Helpers

    getSpacerSize() {
        let entriesTop = this._displayedItemsRef.getBoundingClientRect().top;
        let spacerTop = this._spacerRef.getBoundingClientRect().top;
        return entriesTop - spacerTop;
    }
}

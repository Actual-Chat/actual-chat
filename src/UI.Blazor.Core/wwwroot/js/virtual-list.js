export function create(elementRef, backendRef) {
    return new VirtualList(elementRef, backendRef)
}

export class VirtualList {
    constructor(elementRef, backendRef) {
        this.elementRef = elementRef;
        this.backendRef = backendRef;
        this.abortController = new AbortController();
        this.spacerRef = this.elementRef.querySelector(".spacer");
        this.unmeasuredItemsRef = this.elementRef.querySelector(".items-unmeasured");
        this.displayedItemsRef = this.elementRef.querySelector(".items-displayed");
        this._updateClientSideStateTask = null;
        this._onScrollStoppedTimeout = null;

        let _onScroll = _ => this.updateClientSideStateAsync();
        let listenerOptions = { signal: this.abortController.signal };
        elementRef.addEventListener("scroll", _onScroll, listenerOptions);
        window.addEventListener("resize", _onScroll, listenerOptions);
    };

    dispose() {
        this.abortController.abort();
    }

    afterRender(mustScroll, viewOffset, mustNotifyWhenScrollStops) {
        let spacerSize = this.getSpacerSize();
        if (mustScroll)
            this.elementRef.scrollTo(0, viewOffset + spacerSize);
        let _ = this.updateClientSideStateAsync()
        this.setupOnScroll(mustNotifyWhenScrollStops);
    }

    // Scroll stopped notification

    setupOnScroll(mustNotifyWhenScrollStops) {
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
        this._onScrollStoppedTimeout = setTimeout(() => this.onScrollStopped(), 1000);
    }

    onScrollStopped() {
        let _ = this.updateClientSideStateAsync(true)
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
            renderIndex: parseInt(this.elementRef.dataset["renderIndex"]),
            isScrollStopped: isScrollStopped,
            viewOffset: this.elementRef.scrollTop - spacerSize,
            viewSize: this.elementRef.getBoundingClientRect().height,
            itemSizes: {}
        };
        if (!isScrollStopped) {
            let items = this.elementRef.querySelectorAll(".items-unmeasured .item").values();
            for (let item of items) {
                let key = item.dataset["key"];
                state.itemSizes[key] = item.getBoundingClientRect().height;
            }
            items = this.elementRef.querySelectorAll(".items-displayed .item").values();
            for (let item of items) {
                let key = item.dataset["key"];
                let knownSize = parseFloat(item.dataset["size"])
                let size = item.getBoundingClientRect().height;
                if (Math.abs(size - knownSize) >= 0.001)
                    state.itemSizes[key] = size;
            }
        }
        this._updateClientSideStateTask = this.backendRef.invokeMethodAsync("UpdateClientSideState", state);
    }

    // Helpers

    getSpacerSize() {
        let entriesTop = this.displayedItemsRef.getBoundingClientRect().top;
        let spacerTop = this.spacerRef.getBoundingClientRect().top;
        return entriesTop - spacerTop;
    }
}

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

        let pushMeasurements = e => this.updateClientSideStateAsync(true);
        let listenerOptions = { signal: this.abortController.signal };
        elementRef.addEventListener("scroll", pushMeasurements, listenerOptions);
        window.addEventListener("resize", pushMeasurements, listenerOptions);
    };

    dispose() {
        this.abortController.abort();
    }

    afterRender(mustScroll, viewOffset) {
        let spacerSize = this.getSpacerSize();
        if (mustScroll)
            this.elementRef.scrollTo(0, viewOffset + spacerSize);
        let _ = this.updateClientSideStateAsync()
    }

    async updateClientSideStateAsync(mustMeasureEverything = false) {
        if (this._updateClientSideStateTask != null) {
            // This call should run in the same order / non-concurrently
            await this._updateClientSideStateTask.then(v => v, _ => null);
            this._updateClientSideStateTask = null;
        }
        let spacerSize = this.getSpacerSize();
        let state = {
            renderIndex: parseInt(this.elementRef.dataset["renderIndex"]),
            viewOffset: this.elementRef.scrollTop - spacerSize,
            viewSize: this.elementRef.getBoundingClientRect().height,
            itemSizes: {}
        };
        let items = this.elementRef.querySelectorAll(".items-unmeasured .item").values();
        for (let item of items) {
            let key = item.dataset["key"];
            state.itemSizes[key] = item.getBoundingClientRect().height;
        }
        if (mustMeasureEverything) {
            let items = this.elementRef.querySelectorAll(".items-displayed .item").values();
            for (let item of items) {
                let key = item.dataset["key"];
                let knownSize = parseFloat(item.dataset["size"])
                let size = item.getBoundingClientRect().height;
                if (Math.abs(size - knownSize) >= 0.001)
                    state.itemSizes[key] = size;
            }
        }
        this._updateClientSideStatePromise = this.backendRef.invokeMethodAsync("UpdateClientSideState", state);
    }

    getSpacerSize() {
        let entriesTop = this.displayedItemsRef.getBoundingClientRect().top;
        let spacerTop = this.spacerRef.getBoundingClientRect().top;
        return entriesTop - spacerTop;
    }
}

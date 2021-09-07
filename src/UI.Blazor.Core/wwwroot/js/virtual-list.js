export function create(elementRef, backendRef) {
    return new VirtualList(elementRef, backendRef)
}

export class VirtualList {
    constructor(elementRef, backendRef) {
        this.elementRef = elementRef;
        this.backendRef = backendRef;
        this.abortController = new AbortController();
        this.spacerRef = this.elementRef.querySelector(".spacer");
        this.shownItemsRef = this.elementRef.querySelector(".items-shown");

        let pushMeasurements = e => this.afterRender(true, false, false, 0);
        let listenerOptions = { signal: this.abortController.signal };
        elementRef.addEventListener("scroll", pushMeasurements, listenerOptions);
        window.addEventListener("resize", pushMeasurements, listenerOptions);
    };

    dispose() {
        this.abortController.abort();
    }

    afterRender(mustMeasure, mustMeasureAll, mustScroll, viewOffset) {
        let shownItemsTop = this.shownItemsRef.getBoundingClientRect().top;
        let spacerTop = this.spacerRef.getBoundingClientRect().top;
        let spacerSize = shownItemsTop - spacerTop;
        if (mustScroll)
            this.elementRef.scrollTo(0, viewOffset + spacerSize);
        let state = {
            renderIndex: parseInt(this.elementRef.dataset["renderIndex"]),
            viewOffset: this.elementRef.scrollTop - spacerSize,
            viewSize: this.elementRef.getBoundingClientRect().height,
            itemSizes: {}
        };
        if (mustMeasure) {
            let items = this.elementRef.querySelectorAll(".items-measured .item").values();
            for (let item of items) {
                let key = item.dataset["key"];
                state.itemSizes[key] = item.getBoundingClientRect().height;
            }
        }
        if (mustMeasureAll) {
            let items = this.elementRef.querySelectorAll(".items-shown .item").values();
            for (let item of items) {
                let key = item.dataset["key"];
                state.itemSizes[key] = item.getBoundingClientRect().height;
            }
        }
        this.backendRef.invokeMethodAsync("UpdateClientSideState", state);
    }
}

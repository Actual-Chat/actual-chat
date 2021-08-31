export function create(elementRef, backendRef) {
    return new VirtualList(elementRef, backendRef)
}

export class VirtualList {
    constructor(elementRef, backendRef) {
        this.elementRef = elementRef;
        this.backendRef = backendRef;
        this.abortController = new AbortController();

        let pushStateUpdate = e => this.pushStateUpdate();
        let listenerOptions = { signal: this.abortController.signal };
        elementRef.addEventListener("scroll", pushStateUpdate, listenerOptions);
        window.addEventListener("resize", pushStateUpdate, listenerOptions);
    };

    dispose() {
        this.abortController.abort();
    }

    pushStateUpdate() {
        this.backendRef.invokeMethodAsync("UpdateClientSideState", this.getState());
    }

    getState() {
        return {
            scrollTop: this.elementRef.scrollTop,
            clientHeight: this.elementRef.clientHeight
        };
    }
}

// Mac Catalyst native-overlay renderer: the remote tile is drawn by a native
// AVSampleBufferDisplayLayer positioned over the WKWebView, so this tracks the tile
// element's viewport rect and reports it (CSS px, == webview points) to the native
// overlay host via the owning VideoTrackPlayer. A self-rescheduling rAF read robustly
// follows any movement (scroll of inner containers, layout shifts); interop fires only
// when the rect actually changes.
export class NativeVideoOverlay {
    private readonly element: HTMLElement;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly cornerRadius: number;
    private readonly intersectionObserver: IntersectionObserver;
    private rafHandle = 0;
    private visible = true;
    private lastKey = '';
    private disposed = false;

    static create(element: HTMLElement, blazorRef: DotNet.DotNetObject): NativeVideoOverlay {
        return new NativeVideoOverlay(element, blazorRef);
    }

    constructor(element: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.element = element;
        this.blazorRef = blazorRef;
        this.cornerRadius = parseFloat(getComputedStyle(element).borderTopLeftRadius) || 0;
        this.intersectionObserver = new IntersectionObserver(entries => {
            this.visible = entries[entries.length - 1].isIntersecting;
        });
        this.intersectionObserver.observe(element);
        this.rafHandle = requestAnimationFrame(this.tick);
    }

    public dispose(): void {
        if (this.disposed)
            return;

        this.disposed = true;
        if (this.rafHandle)
            cancelAnimationFrame(this.rafHandle);
        this.intersectionObserver.disconnect();
    }

    // Private methods

    private readonly tick = (): void => {
        if (this.disposed)
            return;

        this.report();
        this.rafHandle = requestAnimationFrame(this.tick);
    };

    private report(): void {
        const r = this.element.getBoundingClientRect();
        const key = `${r.left}|${r.top}|${r.width}|${r.height}|${this.visible ? 1 : 0}`;
        if (key === this.lastKey)
            return;

        this.lastKey = key;
        void this.blazorRef.invokeMethodAsync(
            'OnOverlayRect', r.left, r.top, r.width, r.height, this.visible, this.cornerRadius);
    }
}

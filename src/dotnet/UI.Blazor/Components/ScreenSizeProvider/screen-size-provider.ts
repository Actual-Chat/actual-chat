const LogScope: string = 'ScreenSizeProvider';

export class ScreenSizeProvider {
    private readonly window: Window;
    private size : string;

    static create = (containerDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) => {
        return new ScreenSizeProvider(containerDiv, blazorRef);
    }

    constructor(
        private readonly containerDiv: HTMLDivElement,
        private readonly blazorRef: DotNet.DotNetObject
    ) {
        this.window = window;
        this.window.addEventListener('resize', this.onWindowResize);
        this.size = this.measureSize();
        this.notifySizeChanged(this.size);
    }

    public dispose() {
        this.window.removeEventListener('resize', this.onWindowResize);
    }

    public measureSize(): string {
        let itemDiv : HTMLDivElement = null;
        for (const item of this.containerDiv.children) {
            itemDiv = item as HTMLDivElement;
            if (!item)
                continue;

            const isVisible = window.getComputedStyle(itemDiv).getPropertyValue('width') !== 'auto';
            // console.debug(`${LogScope}.getScreenSize:`, itemDiv.dataset['size'], isVisible);
            if (isVisible)
                return itemDiv.dataset['size'];
        }
        // Returning the last "available" size
        return itemDiv.dataset['size'];
    };

    private onWindowResize = (event: Event) => {
        const size = this.measureSize();
        if (size === this.size)
            return;

        this.size = size;
        this.notifySizeChanged(size);
    };

    private notifySizeChanged(size: string): void {
        console.debug(`${LogScope}: screen size changed to ${size}`);
        this.blazorRef.invokeMethodAsync('OnSizeChanged', size)
    };
}

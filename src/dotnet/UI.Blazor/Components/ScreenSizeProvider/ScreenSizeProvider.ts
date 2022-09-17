const LogScope: string = 'ScreenSizeProvider';

export class ScreenSizeProvider {
    private readonly window: Window;
    private lastScreenSize : string;

    static create = (containerDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) => {
        return new ScreenSizeProvider(containerDiv, blazorRef);
    }

    constructor(
        private readonly containerDiv: HTMLDivElement,
        private readonly blazorRef: DotNet.DotNetObject
    ) {
        this.window = window;
        this.window.addEventListener('resize', this.onWindowResize);
        this.lastScreenSize = this.getScreenSize();
        this.notifySizeChanged(this.lastScreenSize);
    }

    public dispose() {
        this.window.removeEventListener('resize', this.onWindowResize);
    }

    public getScreenSize(): string {
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
        const screenSize = this.getScreenSize();
        if (screenSize === this.lastScreenSize)
            return;

        this.lastScreenSize = screenSize;
        this.notifySizeChanged(screenSize);
    };

    private notifySizeChanged(screenSize: string): void {
        console.debug(`${LogScope}: screen size changed to ${screenSize}`);
        this.blazorRef.invokeMethodAsync('OnSizeChanged', screenSize)
    };
}

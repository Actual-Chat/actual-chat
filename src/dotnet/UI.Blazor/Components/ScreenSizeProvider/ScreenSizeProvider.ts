export class ScreenSizeProvider {
    private containerDiv: HTMLDivElement;
    private blazorRef: DotNet.DotNetObject;
    private readonly window: Window;
    private screenSizeLastValue : string;

    static create(containerDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ScreenSizeProvider {
        return new ScreenSizeProvider(containerDiv, blazorRef);
    }

    constructor(containerDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.containerDiv = containerDiv;
        this.blazorRef = blazorRef;
        this.window = document.defaultView;
        this.window.addEventListener('resize', this.windowResizeListener);
        this.screenSizeLastValue = this.getScreenSize();
        this.notifySizeChanged(this.screenSizeLastValue);
    }

    public getScreenSize = () : string => {
        const children = this.containerDiv.children;
        for (let i = children.length - 1; i >= 0; i--) {
            const element = children[i];
            const divElement = element as HTMLDivElement;
            if (divElement) {
                const style = window.getComputedStyle(divElement);
                const isHidden = style.display === 'none';
                const size = divElement.dataset['size'];
                if (!isHidden)
                    return size;
            }
        }
        return "";
    };

    public dispose() {
        this.window.removeEventListener('resize', this.windowResizeListener);
    }

    private windowResizeListener = ((event: Event) => {
        const newScreenSize = this.getScreenSize();
        if (newScreenSize === this.screenSizeLastValue)
            return;
        this.screenSizeLastValue = newScreenSize;
        this.notifySizeChanged(newScreenSize);
    });

    private notifySizeChanged = ((size : string) => {
        //console.log('size: ' + size);
        this.blazorRef.invokeMethodAsync('OnSizeChanged', size)
    });
}

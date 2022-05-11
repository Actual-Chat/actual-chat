export class EscapeHandler {
    private _blazorRef: DotNet.DotNetObject;
    private _elementRef: HTMLDivElement;

    public static create(elementRef: HTMLDivElement, blazorRef: DotNet.DotNetObject): EscapeHandler {
        return new EscapeHandler(elementRef, blazorRef);
    }

    constructor(element: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._blazorRef = blazorRef;
        this._elementRef = element;
        this._elementRef.addEventListener('keydown', this.onKeyDown);
    }

    public dispose() {
        this._elementRef.removeEventListener('keydown', this.onKeyDown);
        this._elementRef = null;
        this._blazorRef = null;
    }

    private onKeyDown = async (event: KeyboardEvent & { target: Element; }): Promise<void> => {
        if (event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc') {
            await this._blazorRef.invokeMethodAsync('OnEscape');
        }
    };
}

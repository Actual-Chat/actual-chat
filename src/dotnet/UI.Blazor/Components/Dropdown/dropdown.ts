export class Dropdown {

    private _blazorRef: DotNet.DotNetObject;
    private _dropdown: HTMLDivElement;
    private _contentDiv: HTMLDivElement;
    private _contentBody: HTMLDivElement;
    private _controlBtn: HTMLButtonElement;

    static create(dropdown: HTMLDivElement, backendRef: DotNet.DotNetObject): Dropdown {
        return new Dropdown(dropdown, backendRef);
    }

    constructor(dropdown: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._blazorRef = blazorRef;
        this._dropdown = dropdown;
        this._contentDiv = this._dropdown.querySelector('.dropdown-content');
        this._contentBody = this._contentDiv.querySelector('.dropdown-content-body');
        this._controlBtn = this._dropdown.querySelector('.dropdown-button');

        window.addEventListener('mouseup', this.mouseListener)
        document.addEventListener('keydown', this.escapeListener);
    }

    public mouseListener = ((event: MouseEvent & {target: Element; }) => {
        let menu = this._contentDiv;
        let control = this._controlBtn;
        let dropdown = this._dropdown;
        if (control.contains(event.target))
            return;
        if (!dropdown.contains(event.target) && menu.classList.contains('dropdown-content-opened')) {
            this.HideContent(this._blazorRef);
        }
    })

    public escapeListener = ((event: KeyboardEvent & {target: Element; }) => {
        if (event.keyCode == 27 || event.key == "Escape" || event.key == "Esc") {
            let content = this._contentDiv;
            if (content.classList.contains('dropdown-content-opened')) {
                this.HideContent(this._blazorRef);
            }
        }
    })

    public dispose() {
        window.removeEventListener('mouseup', this.mouseListener);
        document.removeEventListener('keydown', this.escapeListener);
    }

    public HideContent(ref: DotNet.DotNetObject) {
        ref.invokeMethodAsync('HideContent');
    }
}

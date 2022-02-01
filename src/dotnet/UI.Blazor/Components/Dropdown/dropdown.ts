import './dropdown.css';

export class Dropdown {

    private _blazorRef: DotNet.DotNetObject;
    private _dropdown: HTMLDivElement;
    private _menuDiv: HTMLDivElement;
    private _menuBody: HTMLDivElement;
    private _controlBtn: HTMLButtonElement;

    static create(dropdown: HTMLDivElement, backendRef: DotNet.DotNetObject): Dropdown {
        return new Dropdown(dropdown, backendRef);
    }

    constructor(dropdown: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._blazorRef = blazorRef;
        this._dropdown = dropdown;
        this._menuDiv = this._dropdown.querySelector('.dropdown-menu');
        this._menuBody = this._menuDiv.querySelector('.menu-body');
        this._controlBtn = this._dropdown.querySelector('.dropdown-button');

        window.addEventListener('mouseup', this.mouseListener)
        document.addEventListener('keydown', this.escapeListener);
    }

    public mouseListener = ((event: MouseEvent & {target: Element; }) => {
        let menu = this._menuDiv;
        let control = this._controlBtn;
        let dropdown = this._dropdown;
        if (control.contains(event.target))
            return;
        if (!dropdown.contains(event.target) && menu.classList.contains('menu-opened')) {
            this.HideMenu(this._blazorRef);
        }
    })

    public escapeListener = ((event: KeyboardEvent & {target: Element; }) => {
        if (event.keyCode == 27 || event.key == "Escape" || event.key == "Esc") {
            let menu = this._menuDiv;
            if (menu.classList.contains('menu-opened')) {
                this.HideMenu(this._blazorRef);
            }
        }
    })

    public dispose() {
        window.removeEventListener('mouseup', this.mouseListener);
        document.removeEventListener('keydown', this.escapeListener);
    }

    public HideMenu(ref: DotNet.DotNetObject) {
        ref.invokeMethodAsync('HideMenu');
    }
}

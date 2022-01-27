import './menu-component.css';

export class Menu {

    private _blazorRef: DotNet.DotNetObject;
    private _menuDiv: HTMLDivElement;
    private _menuBody: HTMLDivElement;
    private _controlBtn: HTMLButtonElement;

    static create(menuDiv: HTMLDivElement, backendRef: DotNet.DotNetObject): Menu {
        return new Menu(menuDiv, backendRef);
    }

    constructor(menuDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._blazorRef = blazorRef;
        this._menuDiv = menuDiv;
        this._menuBody = this._menuDiv.querySelector('.menu-body');
        let menuName = this._menuDiv.getAttribute('name');
        this._controlBtn = document.querySelector('button'+ '.' + menuName);

        window.addEventListener('mouseup', (event: MouseEvent & {target: Element; }) => {
            this.listenerHandler(event);
        });

        document.addEventListener('keydown', (event: KeyboardEvent & {target: Element; }) => {
            if (event.keyCode == 27 || event.key == "Escape" || event.key == "Esc") {
                this.listenerHandler(event);
            }
        });
    }

    public listenerHandler(event: Event & {target: Element}){
        let menu = this._menuDiv;
        let control = this._controlBtn;
        switch (event.type) {
            case "mouseup":
                if (!menu.contains(event.target) && menu.classList.contains('menu-opened')) {
                    if (control.contains(event.target))
                        return;
                    this.HideMenu(this._blazorRef)
                    break;
                }
            case "keydown":
                if (menu.classList.contains('menu-opened'))
                    this.HideMenu(this._blazorRef)
                    break;
            default:
                return;
        }
    }

    public HideMenu(ref: DotNet.DotNetObject) {
        ref.invokeMethodAsync('HideMenu');
    }
}

export class Dropdown {
    private _blazorRef: DotNet.DotNetObject;
    private _ref: HTMLDivElement;
    private _menu: HTMLDivElement;
    private _button: HTMLButtonElement;

    public static create(dropdown: HTMLDivElement, blazorRef: DotNet.DotNetObject): Dropdown {
        return new Dropdown(dropdown, blazorRef);
    }

    constructor(ref: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._blazorRef = blazorRef;
        this._ref = ref;
        this._menu = this._ref.querySelector('.dropdown-menu');
        this._button = this._ref.querySelector('.dropdown-menu');

        window.addEventListener('mouseup', this.onMouseUp);
        document.addEventListener('keydown', this.onKeyDown);
    }

    public dispose() {
        window.removeEventListener('mouseup', this.onMouseUp);
        document.removeEventListener('keydown', this.onKeyDown);
        this._blazorRef = null;
        this._menu = null;
        this._ref = null;
        this._button = null;
    }

    private onMouseUp = ((event: MouseEvent & { target: Element; }) => {
        if (this._button.contains(event.target))
            return;
        if (!this._ref.contains(event.target) && this._menu.classList.contains('dropdown-menu-opened')) {
            void this.toggle(false);
        }
    });

    private onKeyDown = ((event: KeyboardEvent & { target: Element; }) => {
        if (event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc') {
            const content = this._menu;
            if (content.classList.contains('dropdown-menu-opened')) {
                void this.toggle(false);
            }
        }
    });

    private async toggle(mustOpen?: boolean): Promise<void> {
        await this._blazorRef.invokeMethodAsync('Toggle', mustOpen);
    }
}

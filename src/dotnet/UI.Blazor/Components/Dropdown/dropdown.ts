export class Dropdown {
    private _blazorRef: DotNet.DotNetObject;
    private _ref: HTMLDivElement;
    private _dropdownMenu: HTMLDivElement;
    private _menu: HTMLDivElement;
    private _button: HTMLButtonElement;
    private menuObserver: MutationObserver;

    public static create(dropdown: HTMLDivElement, blazorRef: DotNet.DotNetObject): Dropdown {
        return new Dropdown(dropdown, blazorRef);
    }

    constructor(ref: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._blazorRef = blazorRef;
        this._ref = ref;
        this._dropdownMenu = this._ref.querySelector('.dropdown-menu');
        this._menu = this._dropdownMenu.querySelector('.menu');
        this._button = this._ref.querySelector('.dropdown-menu-btn');

        window.addEventListener('mouseup', this.onMouseUp);
        document.addEventListener('keydown', this.onKeyDown);

        let prevState = this._dropdownMenu.classList.contains('dropdown-menu-opened');
        this.menuObserver = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                const { target } = mutation;

                if (mutation.attributeName === 'class') {
                    const elem = mutation.target as HTMLDivElement;
                    const currentState = elem.classList.contains('dropdown-menu-opened');
                    if (prevState !== currentState) {
                        prevState = currentState;
                        this.updateTopMenuPosition();
                    }
                }
            });
        });
        this.menuObserver.observe(this._dropdownMenu, { attributes: true });
    }

    public dispose() {
        window.removeEventListener('mouseup', this.onMouseUp);
        document.removeEventListener('keydown', this.onKeyDown);
        this.menuObserver.disconnect();
        this._blazorRef = null;
        this._dropdownMenu = null;
        this._ref = null;
        this._button = null;
    }

    private updateBottomMenuPosition = () => {
        const limitBottom = document.documentElement.getBoundingClientRect().bottom - 10;
        const menuBottom = this._menu.getBoundingClientRect().bottom;
        let offset = 0;
        if (menuBottom > limitBottom) {
            offset = menuBottom - limitBottom;
            this._menu.style.transform=`translate(0,-${offset}px)`;
        }
    }

    private updateTopMenuPosition = () => {
        const dropdownTop = this._ref.getBoundingClientRect().top;
        const dropdownMenuRect = this._dropdownMenu.getBoundingClientRect();
        const dropdownMenuTop = dropdownMenuRect.top;
        let offset = 0;
        if (dropdownMenuTop > dropdownTop - 7 && dropdownMenuTop != 0) {
            offset = dropdownTop - 7 - dropdownMenuTop;
            this._dropdownMenu.style.transform=`translate(0,${offset}px)`;
        }
        this.updateBottomMenuPosition();
    }



    private onMouseUp = ((event: MouseEvent & { target: Element; }) => {
        if (this._button.contains(event.target)) {
            if (this._dropdownMenu.classList.contains('dropdown-menu-opened'))
                return;
            else {

            }
        }
        if (!this._ref.contains(event.target) && this._dropdownMenu.classList.contains('dropdown-menu-opened')) {
            void this.toggle(false);
        }
    });

    private onKeyDown = ((event: KeyboardEvent & { target: Element; }) => {
        if (event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc') {
            const content = this._dropdownMenu;
            if (content.classList.contains('dropdown-menu-opened')) {
                void this.toggle(false);
            }
        }
    });

    private async toggle(mustOpen?: boolean): Promise<void> {
        await this._blazorRef.invokeMethodAsync('Toggle', mustOpen);
    }
}

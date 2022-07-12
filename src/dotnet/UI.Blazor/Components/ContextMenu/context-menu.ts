export class ContextMenu {
    private _blazorRef: DotNet.DotNetObject;
    private _ref: HTMLDivElement;
    private _menu: HTMLDivElement;
    private _button: HTMLButtonElement;
    private _showObserver: MutationObserver;
    private _hideObserver: MutationObserver;

    public static create(mainDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ContextMenu {
        return new ContextMenu(mainDiv, blazorRef);
    }

    constructor(ref: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this._blazorRef = blazorRef;
        this._ref = ref;
        this._button = this._ref.querySelector('.context-menu-button');

        window.addEventListener('mouseup', this.onMouseUp);
        document.addEventListener('keydown', this.onKeyDown);

        this._showObserver = new MutationObserver(mutations => {
            for (let mutation of mutations) {
                for (let node of mutation.addedNodes) {
                    if (node.nodeName.toLowerCase() == 'div') {
                        let elem = node as HTMLDivElement;
                        if (elem.classList.contains('context-menu-menu')) {
                            this._menu = elem;
                            this.updatePosition(this._menu);
                            this.updateStyle(true);
                        }
                    }
                }
            }
        });

        this._hideObserver = new MutationObserver(mutations => {
            for (let mutation of mutations) {
                for (let node of mutation.removedNodes) {
                    if (node.nodeName.toLowerCase() == 'div') {
                        let elem = node as HTMLDivElement;
                        if (elem.classList.contains('context-menu-menu')) {
                            this._menu = null;
                            this.updateStyle(false);
                        }
                    }
                }
            }
        });

        this._showObserver.observe(this._ref, {childList: true, subtree: true});
        this._hideObserver.observe(this._ref, {childList: true, subtree: true});
    }


    public dispose() {
        window.removeEventListener('mouseup', this.onMouseUp);
        document.removeEventListener('keydown', this.onKeyDown);
        this._showObserver.disconnect();
        this._hideObserver.disconnect();

        this._blazorRef = null;
        this._ref = null;
        this._menu = null;
        this._button = null;
    }

    private updatePosition(menu: HTMLDivElement) {
        const editor = document.querySelector('.chat-message-editor');
        const maxBottom = document.documentElement.clientHeight - editor.clientHeight - 16;
        const maxTop = 60
        const size = this._menu.getBoundingClientRect();
        const bottom = size.bottom;
        const top = size.top;
        if (bottom > maxBottom) {
            let offset = bottom - maxBottom;
            this._menu.style.transform=`translate(0,-${offset}px)`;
        }
        if (top < maxTop) {
            let offset = maxTop - top;
            this._menu.style.transform=`translate(0,${offset}px)`;
        }
    }

    private updateStyle(isOpen: boolean) {
        if (isOpen) {
            this._button.classList.remove('show-on-hover');
            this._button.classList.add('show');
        }
        else {
            this._button.classList.add('show-on-hover');
            this._button.classList.remove('show');
        }
    }

    private onMouseUp = ((event: MouseEvent & { target: Element; }) => {
        if (this._button.contains(event.target))
            return;
        if (!this._ref.contains(event.target) && this._menu != null) {
            void this.toggle(false);
        }
    });

    private onKeyDown = ((event: KeyboardEvent & { target: Element; }) => {
        if (event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc') {
            const content = this._menu;
            if (content != null) {
                void this.toggle(false);
            }
        }
    });

    private async toggle(mustOpen?: boolean): Promise<void> {
        await this._blazorRef.invokeMethodAsync('Toggle', mustOpen);
    }
}

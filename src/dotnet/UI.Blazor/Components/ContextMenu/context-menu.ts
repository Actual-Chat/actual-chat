export class ContextMenu {

    private blazorRef: DotNet.DotNetObject;
    private mainDiv: HTMLDivElement;
    private menu: HTMLDivElement;
    private btn: HTMLButtonElement;
    private topLimit: number = 70;
    private bottomLimit: number;

    public static create(mainDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ContextMenu {
        return new ContextMenu(mainDiv, blazorRef);
    }

    constructor(mainDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.mainDiv = mainDiv;
        this.btn = this.mainDiv.querySelector('.context-button');

        window.addEventListener('mouseup', this.mouseListener);
        document.addEventListener('keydown', this.escapeListener);

        let showObserver = new MutationObserver(mutations => {
            for(let mutation of mutations) {
                for(let node of mutation.addedNodes) {
                    if (node.nodeName.toLowerCase() == 'div') {
                        let elem = node as HTMLDivElement;
                        if (elem.classList.contains('context-menu')) {
                            this.menu = elem;
                            this.alignMenuVertically(this.menu);
                            this.menuOpenedStyle(true);
                        }
                    }
                }
            }
        });

        let hideObserver = new MutationObserver(mutations => {
            for(let mutation of mutations) {
                for(let node of mutation.removedNodes) {
                    if (node.nodeName.toLowerCase() == 'div') {
                        let elem = node as HTMLDivElement;
                        if (elem.classList.contains('context-menu')) {
                            this.menu = null;
                            this.menuOpenedStyle(false);
                        }
                    }
                }
            }
        });
        showObserver.observe(this.mainDiv, {childList: true, subtree: true});
        hideObserver.observe(this.mainDiv, {childList: true, subtree: true});
    }

    private menuOpenedStyle(opened: boolean) {
        const btn = this.btn;
        const menu = this.menu;
        if (opened) {
            const menuSize = menu.getBoundingClientRect();
            const btnSize = btn.getBoundingClientRect();
            const sameBottom = menuSize.bottom == btnSize.bottom;
            const sameTop = menuSize.top == btnSize.top;
            btn.classList.remove('rounded-l-lg');
            btn.classList.remove('border-l');
            if (sameTop)
                menu.classList.remove('rounded-tr-md');
            if (sameBottom)
                menu.classList.remove('rounded-br-md');
        }
        else {
            btn.classList.add('rounded-l-lg');
            btn.classList.add('border-l');
        }
    }

    private alignMenuVertically(menu: HTMLDivElement) {
        this.bottomLimit = document.documentElement.clientHeight - 110
        let size = menu.getBoundingClientRect();
        let bottom = size.bottom;
        if (bottom > this.bottomLimit) {
            let tr = bottom - this.bottomLimit;
            menu.style.transform=`translate(0,-${tr}px)`;
        }
    }

    private mouseListener = ((event: MouseEvent & { target: Element; }) => {
        const { menu, btn, mainDiv } = this;
        if (btn.contains(event.target))
            return;
        if (!mainDiv.contains(event.target) && menu != null) {
            void this.hideContent();
        }
    });

    private escapeListener = ((event: KeyboardEvent & { target: Element; }) => {
        if (event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc') {
            const content = this.menu;
            if (content != null) {
                void this.hideContent();
            }
        }
    });

    private async hideContent(): Promise<void> {
        await this.blazorRef.invokeMethodAsync('HideContent');
    }

    public dispose() {
        window.removeEventListener('mouseup', this.mouseListener);
        document.removeEventListener('keydown', this.escapeListener);
        this.blazorRef = null;
        this.menu = null;
        this.mainDiv = null;
        this.btn = null;
    }
}

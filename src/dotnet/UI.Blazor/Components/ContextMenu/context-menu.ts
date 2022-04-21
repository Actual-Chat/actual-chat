export class ContextMenu {

    private blazorRef: DotNet.DotNetObject;
    private mainDiv: HTMLDivElement;
    private menu: HTMLDivElement;
    private btn: HTMLButtonElement;
    private topLimit: number = 70;
    private bottomLimit: number;
    private menuHeight: number;

    public static create(mainDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ContextMenu {
        return new ContextMenu(mainDiv, blazorRef);
    }

    constructor(mainDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.mainDiv = mainDiv;
        this.btn = this.mainDiv.querySelector('.context-button');

        let showObserver = new MutationObserver(mutations => {
            for(let mutation of mutations) {
                for(let node of mutation.addedNodes) {
                    if (node.nodeName.toLowerCase() == 'div') {
                        let elem = node as HTMLDivElement;
                        if (elem.classList.contains('context-menu')) {
                            this.menu = elem;
                            this.alignMenuVertically(this.menu);
                            this.menuOpenedButtonStyle(true);
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
                            this.menuOpenedButtonStyle(false);
                        }
                    }
                }
            }
        });
        showObserver.observe(this.mainDiv, {childList: true, subtree: true});
        hideObserver.observe(this.mainDiv, {childList: true, subtree: true});
    }

    private menuOpenedButtonStyle(opened: boolean) {
        const btn = this.btn;
        if (opened) {
            btn.classList.remove('rounded-l-md');
            btn.classList.add('bg-accent');
        }
        else {
            btn.classList.add('rounded-l-md');
            btn.classList.remove('bg-accent');
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

    public dispose() {
        this.blazorRef = null;
        this.menu = null;
        this.mainDiv = null;
        this.btn = null;
    }
}

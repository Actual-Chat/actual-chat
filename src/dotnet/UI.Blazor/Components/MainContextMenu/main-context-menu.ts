import './main-context-menu.css';

export class MainContextMenu {
    private blazorRef: DotNet.DotNetObject;
    private menuDiv: HTMLDivElement;
    private direction: string;
    private menu: HTMLDivElement;
    private button: HTMLButtonElement;
    private menuObserver: ResizeObserver;
    private buttonRect: DOMRect;
    private menuRect: DOMRect;
    private buttonQuarter: number;

    public static create(menuDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject, direction: string): MainContextMenu {
        return new MainContextMenu(menuDiv, blazorRef, direction);
    }

    constructor(menuDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject, direction: string) {
        this.menuDiv = menuDiv;
        this.direction = direction;
        const menuId = this.menuDiv.getAttribute('id');
        this.getButton(menuId);
        this.menu = document.querySelector('.main-context-menu');
        this.menuRect = this.menu.getBoundingClientRect();
        this.buttonRect = this.button.getBoundingClientRect();
        // this.printRect(this.menuRect);
        // this.printRect(this.buttonRect);
        this.button.classList.add('selected');

        this.menuObserver = new ResizeObserver(this.resizeObserver);
        this.menuObserver.observe(this.menu);
        this.getQuarter(this.buttonRect);
        if (this.direction == 'row') {
            this.alignRowMenu();
        } else if (this.direction == 'col') {
            this.alignColMenu();
        } else {
            console.log('Direction is wrong');
        }
    }

    private alignRowMenu() {
        if (this.buttonQuarter == 2 || this.buttonQuarter == 4) {
            const buttonLeft = this.buttonRect.left;
            const menuRight = this.menuRect.right;
            console.log('menuRight: ', menuRight);
            const leftOffset = buttonLeft - 5 - menuRight;
            console.log('leftOffset: ', leftOffset);
            this.menu.style.transform=`translate(${leftOffset}px, 0)`;
            console.log('new menuRight: ', this.menu.getBoundingClientRect().right);
        } else {

        }
    }

    private alignColMenu() {

    }

    private getQuarter(divRect: DOMRect) : void {
        const horizontalCenter = divRect.left - (divRect.right - divRect.left) / 2;
        const verticalCenter = divRect.bottom - (divRect.bottom - divRect.top) / 2;
        const displayHeight = document.documentElement.clientHeight;
        const displayWidth = document.documentElement.clientWidth;
        let horizontalHalf = horizontalCenter < displayWidth / 2 ? 'left' : 'right';
        let verticalHalf = verticalCenter < displayHeight / 2 ? 'top' : 'bottom';
        if (horizontalHalf == 'left'){
            this.buttonQuarter = verticalHalf == 'top' ? 1 : 3;
        } else {
            this.buttonQuarter = verticalHalf == 'top' ? 2 : 4;
        }
        console.log('button quarter: ', this.buttonQuarter);
    }

    private getButton(id: string) : void {
        const buttonId = this.menuDiv.getAttribute('id').replace('menu', 'button');
        if (buttonId == null) {
            console.log('Button Id is unavailable.');
            return;
        }
        this.button = document.getElementById(buttonId) as HTMLButtonElement;
    }

    private printRect(elementRect: DOMRect) {
        console.log('element.left: ', elementRect.left);
        console.log('element.right: ', elementRect.right);
        console.log('element.top: ', elementRect.top);
        console.log('element.bottom: ', elementRect.bottom);
    }

    private resizeObserver() {
        console.log('resize observer invoked');
    }

    private dispose() : void {
        this.button.classList.remove('selected');
        this.menuObserver.disconnect();
    }
}

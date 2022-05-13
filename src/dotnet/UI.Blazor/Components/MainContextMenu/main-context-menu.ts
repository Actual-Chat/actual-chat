import './main-context-menu.css';

export class MainContextMenu {
    private blazorRef: DotNet.DotNetObject;
    private menuDiv: HTMLDivElement;
    private direction: string;
    private menu: HTMLDivElement;
    private button: HTMLButtonElement;
    private buttonRect: DOMRect;
    private menuRect: DOMRect;
    private buttonQuarter: number;
    private xOffset: number;
    private yOffset: number;

    public static create(menuDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject, direction: string): MainContextMenu {
        return new MainContextMenu(menuDiv, blazorRef, direction);
    }

    constructor(menuDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject, direction: string) {
        this.blazorRef = blazorRef;
        this.menuDiv = menuDiv;
        this.direction = direction;
        this.getButton();
        this.menu = document.querySelector('.main-context-menu');
        this.menuRect = this.menu.getBoundingClientRect();
        this.buttonRect = this.button.getBoundingClientRect();
        this.button.classList.add('selected');

        window.addEventListener('mouseup', this.onMouseUp);

        this.getQuarter(this.buttonRect);
        if (this.direction == 'row') {
            this.alignRowMenu();
        } else if (this.direction == 'col') {
            this.alignColMenu();
        } else {
            console.log('Direction is wrong');
        }
        this.menu.classList.remove('invisible');
    }

    private onMouseUp = ((event: MouseEvent & { target: Element; }) => {
        if (this.button.contains(event.target))
            return;
        if (!this.menu.contains(event.target) && this.menu != null) {
            this.dispose();
        }
    });

    private alignRowMenu() {
        if (this.buttonQuarter == 2 || this.buttonQuarter == 4) {
            const buttonLeft = this.buttonRect.left;
            const menuRight = this.menuRect.right;
            this.xOffset = buttonLeft - 5 - menuRight;

        } else {
            const buttonRight = this.buttonRect.right;
            const menuLeft = this.menuRect.left;
            this.xOffset = buttonRight + 5 - menuLeft;
        }

        this.getVerticalOffset();
        this.menu.style.transform=`translate(${this.xOffset}px, ${this.yOffset}px)`;
    }

    private getVerticalOffset() :void {
        let delta = 0;
        const buttonTop = this.buttonRect.top;
        const displayHeight = document.documentElement.clientHeight;
        if (buttonTop + this.menu.clientHeight >= displayHeight - 10) {
            delta = displayHeight - buttonTop - this.menu.clientHeight - 10;
        }
        this.yOffset = buttonTop + delta;
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
    }

    private getButton() : void {
        if (this.menuDiv == null) {
            return;
        }
        const buttonId = this.menuDiv.getAttribute('id').replace('menu', 'button');
        if (buttonId == null) {
            console.log('Button Id is unavailable.');
            return;
        }
        this.button = document.getElementById(buttonId) as HTMLButtonElement;
    }

    private closeMenu(): void {
        this.button.classList.remove('selected');
        this.button.classList.remove('selected');
        this.menu.style.transform=`translate(0px, 0px)`;
        window.removeEventListener('mouseup', this.onMouseUp)
        this.menu.classList.add('invisible');
        this.blazorRef.invokeMethodAsync('CloseMenu');
    }

    private dispose() : void {
        this.closeMenu();
    }
}

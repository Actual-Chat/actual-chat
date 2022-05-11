import './main-context-menu.css';

export class MainContextMenu {
    private blazorRef: DotNet.DotNetObject;
    private menuDiv: HTMLDivElement;
    private menuObserver : ResizeObserver;

    public static create(menuDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): MainContextMenu {
        return new MainContextMenu(menuDiv, blazorRef);
    }

    constructor(menuDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.menuDiv = menuDiv;
        this.menuObserver = new ResizeObserver(this.updateInnerMenu);
        this.menuObserver.observe(this.menuDiv);
    }

    private dispose() : void {
        // this.feedbackDiv.removeEventListener('mouseenter', this.feedbackEnterListener);
        // this.feedbackDiv.removeEventListener('mouseleave', this.feedbackLeaveListener);
        // for (let i = 0; i < this.stars.length; i++) {
        //     let star = this.stars[i];
        //     star.removeEventListener('mouseenter', this.starEnterListener);
        // }
    }

    private updateInnerMenu() {
        console.log('this.updateInnerMenu method invoked');
    }
}

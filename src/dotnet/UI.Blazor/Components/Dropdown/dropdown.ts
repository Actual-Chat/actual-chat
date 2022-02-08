export class Dropdown {

    private blazorRef: DotNet.DotNetObject;
    private dropdown: HTMLDivElement;
    private contentDiv: HTMLDivElement;
    private contentBody: HTMLDivElement;
    private controlBtn: HTMLButtonElement;

    static create(dropdown: HTMLDivElement, blazorRef: DotNet.DotNetObject): Dropdown {
        return new Dropdown(dropdown, blazorRef);
    }

    constructor(dropdown: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.dropdown = dropdown;
        this.contentDiv = this.dropdown.querySelector('.dropdown-content');
        this.contentBody = this.contentDiv.querySelector('.dropdown-content-body');
        this.controlBtn = this.dropdown.querySelector('.dropdown-button');

        window.addEventListener('mouseup', this.mouseListener)
        document.addEventListener('keydown', this.escapeListener);
    }

    private mouseListener = ((event: MouseEvent & {target: Element; }) => {
        const menu = this.contentDiv;
        const control = this.controlBtn;
        const dropdown = this.dropdown;
        if (control.contains(event.target))
            return;
        if (!dropdown.contains(event.target) && menu.classList.contains('dropdown-content-opened')) {
            this.hideContent();
        }
    })

    private escapeListener = ((event: KeyboardEvent & {target: Element; }) => {
        if (event.keyCode == 27 || event.key == "Escape" || event.key == "Esc") {
            const content = this.contentDiv;
            if (content.classList.contains('dropdown-content-opened')) {
                this.hideContent();
            }
        }
    })

    private dispose() {
        window.removeEventListener('mouseup', this.mouseListener);
        document.removeEventListener('keydown', this.escapeListener);
    }

    private hideContent() {
        this.blazorRef.invokeMethodAsync('HideContent');
    }
}

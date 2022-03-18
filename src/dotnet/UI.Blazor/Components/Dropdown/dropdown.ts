export class Dropdown {

    private blazorRef: DotNet.DotNetObject;
    private dropdown: HTMLDivElement;
    private contentDiv: HTMLDivElement;
    private controlBtn: HTMLButtonElement;

    public static create(dropdown: HTMLDivElement, blazorRef: DotNet.DotNetObject): Dropdown {
        return new Dropdown(dropdown, blazorRef);
    }

    constructor(dropdown: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.dropdown = dropdown;
        this.contentDiv = this.dropdown.querySelector('.dropdown-content');
        this.controlBtn = this.dropdown.querySelector('.dropdown-button');

        window.addEventListener('mouseup', this.mouseListener);
        document.addEventListener('keydown', this.escapeListener);
    }

    private mouseListener = ((event: MouseEvent & { target: Element; }) => {
        const { contentDiv, controlBtn, dropdown } = this;
        if (controlBtn.contains(event.target))
            return;
        if (!dropdown.contains(event.target) && contentDiv.classList.contains('dropdown-content-opened')) {
            void this.hideContent();
        }
    });

    private escapeListener = ((event: KeyboardEvent & { target: Element; }) => {
        if (event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc') {
            const content = this.contentDiv;
            if (content.classList.contains('dropdown-content-opened')) {
                void this.hideContent();
            }
        }
    });

    public dispose() {
        window.removeEventListener('mouseup', this.mouseListener);
        document.removeEventListener('keydown', this.escapeListener);
        this.blazorRef = null;
        this.contentDiv = null;
        this.dropdown = null;
        this.controlBtn = null;
    }

    private async hideContent(): Promise<void> {
        await this.blazorRef.invokeMethodAsync('HideContent');
    }
}

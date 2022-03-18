import './navbar.css';

export class Navbar {
    private blazorRef: DotNet.DotNetObject;
    private navbarDiv: HTMLElement;
    private buttons: NodeListOf<Element>
    private menus: NodeListOf<Element>;

    public static create(navbarDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): Navbar {
        return new Navbar(navbarDiv, blazorRef);
    }

    constructor(navbarDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.navbarDiv = navbarDiv;
        this.buttons = this.navbarDiv.querySelectorAll('.navbar-menu-icon');
        this.menus = this.navbarDiv.querySelectorAll('.navbar-menu-group');
        for (let i = 0; i < this.buttons.length; i++) {
            this.buttons[i].addEventListener('mousedown', this.buttonMouseDownListener)
        }
        let chats = this.navbarDiv.querySelector('.navbar-chats');
        chats.classList.replace('hidden', 'flex');
    }

    public getMenuClass(buttonClass: string) : string {
        return '.' + buttonClass.substring(0, buttonClass.length - 9);
    }

    private buttonMouseDownListener = (event: MouseEvent & {target: Element; }) : void => {
        let button = event.currentTarget as HTMLButtonElement;
        let cls = '';
        for (let i = 0; i < button.classList.length; i++) {
            cls = button.classList[i].toString();
            if (cls.includes('-shortcut'))
                break;
        }
        if (cls == '') return;
        let menuClass = this.getMenuClass(cls);
        if (menuClass == '') return;
        for (let i = 0; i < this.menus.length; i++) {
            let elem = this.menus[i];
            if (elem.classList.contains(menuClass.substring(1)))
                elem.classList.replace('hidden', 'flex')
            else
                elem.classList.replace('flex', 'hidden');
        }
    }

    private dispose() : void {
    }
}

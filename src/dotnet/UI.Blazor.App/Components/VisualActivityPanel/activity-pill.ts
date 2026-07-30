import { Disposable } from 'disposable';

// Reparents the pill from its header-slot render position into
// `.layout-body-wrapper > .c-container`, which is `position: relative` and whose
// bounds match the middle panel (right edge = left edge of the right side-nav
// when open; top edge = below header/subheader). No JS measurement needed —
// CSS `position: absolute` does the rest.
export class ActivityPill implements Disposable {
    private readonly home: HTMLElement | null;

    public static create(element: HTMLElement): ActivityPill {
        return new ActivityPill(element);
    }

    constructor(private readonly element: HTMLElement) {
        this.home = element.parentElement;
        const host = document.querySelector('.layout-body-wrapper > .c-container');
        (host ?? document.body).appendChild(element);
    }

    public dispose(): void {
        if (this.home?.isConnected)
            this.home.appendChild(this.element);
        else
            this.element.remove();
    }
}

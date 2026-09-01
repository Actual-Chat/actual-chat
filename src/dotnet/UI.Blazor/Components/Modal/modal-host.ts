import { unselect } from 'keyboard';

export class ModalHost {
    private static inertedElements: HTMLElement[] = [];

    public static updateBodyStyle(hasModals: boolean) {
        if (hasModals)
            unselect();
        document.body.style.overflow = hasModals ? 'hidden' : 'auto';
    }

    // Isolates the topmost modal from the rest of the page by making everything
    // outside its DOM subtree `inert`, so background shortcuts/focus are disabled.
    public static updateInert(topmostModalId: string): void {
        for (const element of ModalHost.inertedElements)
            element.removeAttribute('inert');
        ModalHost.inertedElements = [];

        if (!topmostModalId)
            return;

        const modal = document.getElementById(topmostModalId);
        if (!modal)
            return;

        for (
            let node: HTMLElement = modal;
            node !== document.body && node.parentElement;
            node = node.parentElement
        ) {
            for (const sibling of Array.from(node.parentElement.children)) {
                if (sibling === node || !(sibling instanceof HTMLElement))
                    continue;
                // data-no-inert opts an element out of inerting: a floating-layer host
                // (e.g. the menu host), so dropdowns opened from within the modal stay
                // interactive, or a global hotkey that must fire on top of a modal.
                if (sibling.hasAttribute('data-modal')
                    || sibling.hasAttribute('data-no-inert')
                    || sibling.hasAttribute('inert'))
                    continue;

                sibling.setAttribute('inert', '');
                ModalHost.inertedElements.push(sibling);
            }
        }
    }
}

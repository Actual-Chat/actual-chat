import { unselect } from 'keyboard';

export class ModalHost {
    public static updateBodyStyle(hasModals: boolean) {
        if (hasModals)
            unselect();
        document.body.style.overflow = hasModals ? 'hidden' : 'auto';
    }
}

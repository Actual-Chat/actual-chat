export class ModalHost {
    public static updateBodyStyle(hasModals: boolean) {
        document.body.style.overflow = hasModals ? 'hidden' : 'auto';
    }
}

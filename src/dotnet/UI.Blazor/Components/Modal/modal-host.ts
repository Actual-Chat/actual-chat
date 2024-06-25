export class ModalHost {
    public static updateBodyStyle(hasModals: boolean) {
        document.body.style.overflow = hasModals ? 'hidden' : 'auto';
    }

    public static hideModal(id: string) {
        let modalOverlay = document.getElementById(id);
        if (modalOverlay == null)
            return;
        modalOverlay.classList.add('hide');
    }
}

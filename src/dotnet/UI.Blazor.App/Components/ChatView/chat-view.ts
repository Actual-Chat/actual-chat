// Wires the chat view's PageUp/PageDown shortcut buttons (clicked by keyux)
// to scroll the chat's virtual list, without a Blazor round-trip.
export function initChatViewScroll(): void {
    window.addEventListener('click', event => {
        const target = event.target as HTMLElement | null;
        const button = target?.closest<HTMLElement>('[data-chat-scroll]');
        if (!button)
            return;

        const list = button.parentElement?.querySelector<HTMLElement>('.virtual-list');
        if (!list)
            return;

        const direction = button.dataset.chatScroll === 'up' ? -1 : 1;
        list.scrollBy({ top: direction * list.clientHeight * 0.9, behavior: 'smooth' });
    });
}

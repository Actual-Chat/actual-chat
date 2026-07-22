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
    // keyux ignores hotkeys inside editables, and the message editor is focused
    // by default, so PageUp/PageDown are re-routed to the scroll buttons here.
    window.addEventListener('keydown', event => {
        if (event.key !== 'PageUp' && event.key !== 'PageDown')
            return;

        const target = event.target as HTMLElement | null;
        if (!target?.isContentEditable || !target.closest('.chat-message-editor'))
            return;

        const direction = event.key === 'PageUp' ? 'up' : 'down';
        const button = document.querySelector<HTMLElement>(`[data-chat-scroll="${direction}"]`);
        if (!button)
            return;

        event.preventDefault();
        button.click();
    });
}

import { DeviceInfo } from 'device-info';

export function hasModifierKey(event: KeyboardEvent | MouseEvent | WheelEvent): boolean {
    return event.altKey || event.shiftKey || event.ctrlKey || event.metaKey;
}

export function isEscapeKey(event: KeyboardEvent): boolean {
    return event.key === 'Escape' || event.key === 'Esc';
}

export function unselect(everything = false): void {
    if (!DeviceInfo.isMobile)
        return;

    /*
    const activeElement = document.activeElement;
    if (everything) {
        if (activeElement instanceof HTMLInputElement || activeElement instanceof HTMLTextAreaElement)
            activeElement.blur();
    }
     */

    const selection = window.getSelection();
    if (!selection || selection.isCollapsed)
        return;

    if (!everything) {
        const node = selection.anchorNode;
        if (node instanceof HTMLElement && node.isContentEditable)
            return;
        if (node?.parentElement?.closest('[contenteditable="true"]'))
            return;
    }

    selection.removeAllRanges();
}

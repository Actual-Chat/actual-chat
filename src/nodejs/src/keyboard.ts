import { DeviceInfo } from 'device-info';

export function hasModifierKey(event: KeyboardEvent | MouseEvent | WheelEvent): boolean {
    return event.altKey || event.shiftKey || event.ctrlKey || event.metaKey;
}

export function isEscapeKey(event: KeyboardEvent): boolean {
    return event.key === 'Escape' || event.key === 'Esc';
}

export function unselect(): void {
    if (!DeviceInfo.isMobile)
        return;

    const activeElement = document.activeElement;
    if (activeElement instanceof HTMLInputElement
        || activeElement instanceof HTMLTextAreaElement
        || (activeElement instanceof HTMLElement && activeElement.isContentEditable))
        return;

    window.getSelection()?.removeAllRanges();
}

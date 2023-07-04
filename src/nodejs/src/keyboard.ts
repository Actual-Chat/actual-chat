export function hasModifierKey(event: KeyboardEvent | MouseEvent | WheelEvent): boolean {
    return event.altKey || event.shiftKey || event.ctrlKey || event.metaKey;
}

export function isEscapeKey(event: KeyboardEvent): boolean {
    return event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc';
}

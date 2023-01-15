export function hasModifierKey(event: KeyboardEvent | MouseEvent | WheelEvent): boolean {
    return event.altKey || event.shiftKey || event.ctrlKey || event.metaKey;
}

import { focusGroupKeyUX, hotkeyKeyUX, hotkeyMacCompat, startKeyUX } from 'keyux';

let started = false;

// Bootstraps keyux — app-wide keyboard shortcuts driven by `aria-keyshortcuts`.
// Escape is handled here rather than via keyux, since keyux ignores it inside
// text inputs, while Escape must still close modals/search/menus while typing.
export function initKeyboardUI(): void {
    if (started)
        return;

    started = true;
    startKeyUX(window, [
        hotkeyKeyUX([hotkeyMacCompat()]),
        focusGroupKeyUX(),
    ]);
    window.addEventListener('keydown', onEscape, { capture: true });
    window.addEventListener('keydown', onOptionToggle);
}

// Toggles a focused listbox option on Space/Enter/X. Registered before keyux's
// focus-group listener so stopImmediatePropagation suppresses its type-ahead.
function onOptionToggle(event: KeyboardEvent): void {
    if (event.isComposing)
        return;

    const target = event.target as HTMLElement | null;
    if (target?.getAttribute('role') !== 'option')
        return;
    if (event.key !== ' ' && event.key !== 'Enter' && event.key !== 'x' && event.key !== 'X')
        return;

    event.preventDefault();
    event.stopImmediatePropagation();
    target.click();
}

function onEscape(event: KeyboardEvent): void {
    if (event.key !== 'Escape' || event.isComposing)
        return;

    const target = findEscapeTarget();
    if (!target)
        return;

    event.preventDefault();
    event.stopImmediatePropagation();
    target.click();
}

// Returns the last (topmost in DOM order) non-inert Escape handler.
function findEscapeTarget(): HTMLElement | null {
    const targets = document.querySelectorAll<HTMLElement>('[aria-keyshortcuts="escape" i]');
    for (let i = targets.length - 1; i >= 0; i--) {
        if (!isInert(targets[i]))
            return targets[i];
    }
    return null;
}

function isInert(node: HTMLElement): boolean {
    for (let e: HTMLElement | null = node; e && e.tagName !== 'BODY'; e = e.parentElement) {
        if (e.hasAttribute('inert') || e.getAttribute('aria-hidden') === 'true')
            return true;
    }
    return false;
}

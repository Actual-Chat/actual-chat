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
    window.addEventListener('keydown', onFocusModality, { capture: true });
    window.addEventListener('pointerdown', onPointerDown, { capture: true });
    window.addEventListener('keydown', onEscape, { capture: true });
    window.addEventListener('keydown', onActivate);
}

// Tracks input modality so the focus ring (gated by body.keyboard-focus in CSS)
// shows only during keyboard navigation. Escape and pointer input clear it.
function onFocusModality(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
        document.body.classList.remove('keyboard-focus');
        return;
    }

    const target = event.target as HTMLElement | null;
    const isEditable = !!target && (target.isContentEditable
        || target.tagName === 'INPUT'
        || target.tagName === 'TEXTAREA');
    const movesFocus = event.key === 'Tab'
        || (!isEditable && (event.key.startsWith('Arrow') || event.key === 'Home' || event.key === 'End'));
    if (movesFocus)
        document.body.classList.add('keyboard-focus');
}

function onPointerDown(): void {
    document.body.classList.remove('keyboard-focus');
}

// Activates a focused custom control by synthesizing a click: role="button"
// (non-native) on Enter/Space, role="option" on Enter/Space/X. Registered
// before keyux's focus-group listener so stopImmediatePropagation suppresses
// its type-ahead.
function onActivate(event: KeyboardEvent): void {
    if (event.isComposing)
        return;

    const target = event.target as HTMLElement | null;
    if (!target)
        return;

    const role = target.getAttribute('role');
    const isOption = role === 'option';
    const isCustomButton = role === 'button' && target.tagName !== 'BUTTON' && target.tagName !== 'A';
    if (!isOption && !isCustomButton)
        return;

    const isActivateKey = event.key === 'Enter' || event.key === ' '
        || (isOption && (event.key === 'x' || event.key === 'X'));
    if (!isActivateKey)
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

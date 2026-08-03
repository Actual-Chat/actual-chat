import { focusGroupKeyUX, hotkeyKeyUX, hotkeyMacCompat, startKeyUX } from 'keyux';
import { DeviceInfo } from 'device-info';

const ChordTimeoutMs = 2000;
const ModifierKeys = new Set(['Control', 'Alt', 'Shift', 'Meta', 'AltGraph', 'CapsLock']);

let started = false;
let chordLeader = '';
let chordTimeoutHandle: ReturnType<typeof setTimeout> | null = null;

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
    window.addEventListener('keydown', onChord, { capture: true });
    window.addEventListener('keydown', onFocusModality, { capture: true });
    window.addEventListener('pointerdown', onPointerDown, { capture: true });
    window.addEventListener('keydown', onEscape, { capture: true });
    window.addEventListener('keydown', onActivate);
}

// Two-key chords, which keyux can't express. While armed, the next key is always
// consumed - Escape included, and it just cancels; a still-held Shift is ignored.
function onChord(event: KeyboardEvent): void {
    if (event.isComposing || ModifierKeys.has(event.key))
        return;

    const codes = getKeyCodes(event);
    if (chordLeader) {
        const keys = codes.flatMap(code => code.startsWith('shift+') ? [code, code.slice(6)] : [code]);
        const target = findChordTarget(keys.map(key => `${chordLeader} ${key}`));
        endChord();
        event.preventDefault();
        event.stopImmediatePropagation();
        target?.click();
        return;
    }

    const leader = findChordLeader(codes);
    if (!leader)
        return;

    chordLeader = leader;
    chordTimeoutHandle = setTimeout(endChord, ChordTimeoutMs);
    event.preventDefault();
    event.stopImmediatePropagation();
}

function endChord(): void {
    chordLeader = '';
    if (chordTimeoutHandle === null)
        return;

    clearTimeout(chordTimeoutHandle);
    chordTimeoutHandle = null;
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
    endChord();
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

    const target = findLast('[aria-keyshortcuts="escape" i]');
    if (!target)
        return;

    event.preventDefault();
    event.stopImmediatePropagation();
    target.click();
}

// Private methods

function findChordLeader(codes: string[]): string | null {
    for (const code of codes) {
        if (findLast(`[data-hotkey-chord^="${code} " i]`))
            return code;
    }

    return null;
}

function findChordTarget(chords: string[]): HTMLElement | null {
    for (const chord of chords) {
        const target = findLast(`[data-hotkey-chord="${chord}" i]`);
        if (target)
            return target;
    }

    return null;
}

// Returns the last (topmost in DOM order) non-inert match.
function findLast(selector: string): HTMLElement | null {
    const targets = document.querySelectorAll<HTMLElement>(selector);
    for (let i = targets.length - 1; i >= 0; i--) {
        if (!isInert(targets[i]))
            return targets[i];
    }

    return null;
}

// Mirrors keyux's combo format, incl. its ⌘-for-Ctrl macOS compat and its
// event.code fallback for non-English layouts.
function getKeyCodes(event: KeyboardEvent): string[] {
    let prefix = '';
    if (event.metaKey)
        prefix += 'meta+';
    if (event.ctrlKey)
        prefix += 'ctrl+';
    if (event.altKey)
        prefix += 'alt+';
    if (event.shiftKey)
        prefix += 'shift+';
    if (DeviceInfo.isMacOS && !prefix.includes('meta+ctrl'))
        prefix = prefix.replace('meta+', 'ctrl+');

    const key = event.key.toLowerCase();
    const codes = [prefix + key];
    if (/^(Key.|Digit\d)$/.test(event.code)) {
        const enKey = event.code.replace(/^Key|^Digit/, '').toLowerCase();
        if (enKey !== key)
            codes.push(prefix + enKey);
    }

    return codes;
}

function isInert(node: HTMLElement): boolean {
    for (let e: HTMLElement | null = node; e && e.tagName !== 'BODY'; e = e.parentElement) {
        if (e.hasAttribute('inert') || e.getAttribute('aria-hidden') === 'true')
            return true;
    }

    return false;
}

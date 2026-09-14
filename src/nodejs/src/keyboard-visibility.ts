import { DeviceInfo } from 'device-info';

// Toggles `body.keyboard-open` while the on-screen keyboard is up, so CSS can react to keyboard
// *visibility* rather than mere focus. Detection mirrors the chat editor's proven heuristic:
// capture the visual-viewport height when an editable first gains focus, then treat a sustained
// shrink past a threshold as the keyboard. Because it watches the viewport (not focus), hiding the
// keyboard while a field stays focused - e.g. Android's keyboard-close button - reads as closed.
const MinShrinkPx = 100;

let baseline: number | null = null;
let lastWidth = 0;

function isEditable(node: EventTarget | null): boolean {
    const el = node as HTMLElement | null;
    return !!el && (el.isContentEditable || el.tagName === 'INPUT' || el.tagName === 'TEXTAREA');
}

function viewportHeight(): number {
    return window.visualViewport?.height ?? window.innerHeight;
}

function update(): void {
    const width = window.visualViewport?.width ?? window.innerWidth;
    if (width !== lastWidth) {
        // Orientation/window resize: re-baseline so a stale reference doesn't misreport the keyboard.
        lastWidth = width;
        if (baseline != null)
            baseline = viewportHeight();
    }
    const isOpen = baseline != null && baseline - viewportHeight() > MinShrinkPx;
    document.body.classList.toggle('keyboard-open', isOpen);
}

export function initKeyboardVisibility(): void {
    if (!DeviceInfo.isMobile || !window.visualViewport)
        return;

    document.addEventListener('focusin', e => {
        if (!isEditable(e.target))
            return;
        // Keep the pre-keyboard baseline across field-to-field moves: capture it only when none is
        // active, so refocusing another field mid-typing doesn't reset the reference to a shrunk height.
        baseline ??= viewportHeight();
        update();
    });
    document.addEventListener('focusout', e => {
        // Focus moving to another editable keeps the keyboard up; only a move to a non-editable
        // (or to nothing) tears it down.
        if (isEditable(e.relatedTarget))
            return;
        baseline = null;
        update();
    });
    // The keyboard resizes the visual viewport; panning it fires scroll rather than resize.
    window.visualViewport.addEventListener('resize', update);
    window.visualViewport.addEventListener('scroll', update);
}

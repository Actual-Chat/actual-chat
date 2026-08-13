import { getLogs } from 'logging';

const { debugLog } = getLogs('FocusUI');

export class FocusUI {
    // preventScroll everywhere we focus programmatically: iOS otherwise scrolls the element
    // into view and shrinks Safari's toolbars, which is the jump the CSS opacity-blink hack
    // used to mask. WebKit 236584 fixed preventScroll on iOS in Safari 15.5, ten months
    // before our 16.4 minimum, so the hack is no longer needed.
    public static focus(targetRef: HTMLElement): void {
        debugLog?.log(`focus, target:`, targetRef)
        targetRef.focus({ preventScroll: true });
    }

    public static blur(): void {
        debugLog?.log(`blur()`);
        const activeElement = document.activeElement as HTMLElement;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (activeElement?.blur != null)
            activeElement.blur();
    }
}

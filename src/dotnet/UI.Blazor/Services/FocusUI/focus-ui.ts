import { getLogs } from 'logging';

const { debugLog } = getLogs('FocusUI');

export class FocusUI {
    public static focus(targetRef: HTMLElement): void {
        debugLog?.log(`focus, target:`, targetRef)
        targetRef.focus();
    }

    public static blur(): void {
        debugLog?.log(`blur()`);
        const activeElement = document.activeElement as HTMLElement;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (activeElement?.blur != null)
            activeElement.blur();
    }
}

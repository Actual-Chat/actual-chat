/** Stand-in for `-webkit-app-region: drag` on the AppKit host, where the page extends under the
 *  titlebar: a press on a `data-window-drag` element that moves past a small threshold asks the
 *  host to move the window, a double click on its non-interactive part asks for the titlebar
 *  double-click action. Clicks stay clicks, and the click WebKit still synthesizes when a window
 *  drag ends is swallowed. The host registers `windowDrag` only on macOS. */

interface WindowDragHost {
    postMessage(message: 'drag' | 'zoom'): void;
}

interface WebKitGlobals {
    webkit?: { messageHandlers?: { windowDrag?: WindowDragHost } };
}

const DragThreshold = 4;
const Controls = 'a, button, input, textarea, select, [contenteditable="true"]';

export const WindowDrag = {
    init(): void {
        const host = (globalThis as WebKitGlobals).webkit?.messageHandlers?.windowDrag;
        if (!host)
            return;

        let isDragging = false;
        document.addEventListener('click', e => {
            if (!isDragging)
                return;

            isDragging = false;
            e.stopImmediatePropagation();
            e.preventDefault();
        }, true);
        document.addEventListener('mousedown', e => {
            isDragging = false;
            if (e.button !== 0 || !(e.target instanceof Element))
                return;

            const region = e.target.closest('[data-window-drag]');
            if (!region || isInside(e.target, region, Controls))
                return;

            if (e.detail === 2) {
                if (!isInside(e.target, region, '[role="button"]'))
                    host.postMessage('zoom');
                return;
            }

            const startX = e.clientX;
            const startY = e.clientY;
            const onMove = (m: MouseEvent) => {
                if (Math.abs(m.clientX - startX) < DragThreshold && Math.abs(m.clientY - startY) < DragThreshold)
                    return;

                stop();
                isDragging = true;
                host.postMessage('drag');
            };
            const stop = () => {
                window.removeEventListener('mousemove', onMove, true);
                window.removeEventListener('mouseup', stop, true);
            };
            window.addEventListener('mousemove', onMove, true);
            window.addEventListener('mouseup', stop, true);
        }, true);
    },
};

function isInside(target: Element, region: Element, selector: string): boolean {
    const match = target.closest(selector);
    return match !== null && match !== region && region.contains(match);
}

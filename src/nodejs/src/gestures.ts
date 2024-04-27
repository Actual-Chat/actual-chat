import { DeviceInfo } from 'device-info';
import { DisposableBag } from 'disposable';
import { DocumentEvents, preventDefaultForEvent, stopEvent } from 'event-handling';
import { fromEvent } from 'rxjs';
import { getOrInheritData } from 'dom-helpers';
import { History } from '../../dotnet/UI.Blazor/Services/History/history';
import { FocusUI } from '../../dotnet/UI.Blazor/Services/FocusUI/focus-ui';
import { ScreenSize } from '../../dotnet/UI.Blazor/Services/ScreenSize/screen-size';
import { Timeout } from 'timeout';
import { Tune, TuneName, TuneUI } from '../../dotnet/UI.Blazor/Services/TuneUI/tune-ui';
import { Vector2D } from 'math';
import { Log } from 'logging';

const { debugLog } = Log.get('Gestures');

export type GestureEvent = PointerEvent | MouseEvent | TouchEvent | WheelEvent;

export class Gestures {
    public static activeGestures = new Set<Gesture>();
    private static dumpTimeout: Timeout | null;

    public static init(): void {
        // Used gestures
        DataHrefGesture.use();
        SuppressDefaultContextMenuGesture.use();
        ContextMenuGesture.use();
    }

    public static addActive(gesture: Gesture): Gesture {
        debugLog?.log(`+ `, gesture);
        this.activeGestures.add(gesture);
        this.startDumping();
        return gesture;
    }

    public static removeActive(gesture: Gesture): Gesture {
        debugLog?.log(`- `, gesture);
        this.activeGestures.delete(gesture);
        return gesture;
    }

    // Private methods

    private static startDumping() {
        if (debugLog == null || this.activeGestures.size == 0 || this.dumpTimeout != null)
            return;

        this.dumpTimeout = new Timeout(1000, () => this.dumpTracked());
    }

    private static dumpTracked() {
        if (this.activeGestures.size == 0)
            debugLog?.log(`No active gestures`);
        else {
            debugLog?.log(`Active gestures:`);
            for (const gesture of this.activeGestures)
                debugLog?.log(`  `, gesture);
        }
        this.dumpTimeout = null;
        this.startDumping();
    }
}

export class Gesture extends DisposableBag {
    public dispose() {
        if (this.isDisposed)
            return;

        try {
            super.dispose();
        }
        finally {
            Gestures.removeActive(this);
        }
    }
}

class DataHrefGesture extends Gesture {
    public static get blazor() {
        return globalThis['Blazor'] as { navigateTo: (url: string) => void };
    }

    public static use(): void {
        debugLog?.log(`DataHrefGesture.use`);

        DocumentEvents.active.pointerDown$.subscribe((event: PointerEvent) => {
            if (event.button !== 0) // Only primary button on wide screen
                return;

            debugLog?.log(`DataHrefGesture.use: pointerDown:`, event);
            this.tryHandle(event, false);
        });

        // We attach this event to pointerUp instead of click solely because
        // click somehow doesn't trigger on iOS for such divs, and none of
        // suggested workarounds helped.
        //
        // For issue w/ click & workarounds, see "Safari Mobile" section here:
        // - https://developer.mozilla.org/en-US/docs/Web/API/Element/click_event
        DocumentEvents.active.pointerUp$.subscribe((event: PointerEvent) => {
            if (event.button !== 0) // Only primary button
                return;

            debugLog?.log(`DataHrefGesture.use: pointerUp:`, event);
            this.tryHandle(event, true);
        });
    }

    public static tryHandle(event: Event, isPointerUp: boolean) {
        // ContextMenuGesture's capturing handler may cancel this event
        if (event.defaultPrevented)
            return;

        const [element, href] = getOrInheritData(event.target, 'href');
        if (href === null)
            return;

        const target = event.target as HTMLElement;
        if (target && target.closest('div.pulling')) {
            // Do not trigger navigation during side-nav pulling
            return;
        }

        // Check if we can process it as part of pointerDown event
        const [triggerElement, menuRef] = getOrInheritData(event.target, 'menu');
        let menuTrigger = 0;
        if (triggerElement && menuRef)
            menuTrigger = parseInt(triggerElement.dataset['menuTrigger'] ?? '2');
        const requiresPointerUp = !ScreenSize.isWide() || menuTrigger !== 2;
        if (isPointerUp !== requiresPointerUp)
            return;

        debugLog?.log(`DataHrefGesture: navigating on data href:`, href);
        const tune = Tune[element?.dataset['hrefTune'] as TuneName];
        FocusUI.blur();
        if (tune)
            TuneUI.play(tune);
        if (href.startsWith('http://') || href.startsWith('https://'))
            location.href = href; // External URL
        else {
            const replaceOnPrefix = element.dataset['replaceOnPrefix'];
            let mustReplace = false;
            if (replaceOnPrefix) {
                const url = new URL(location.href);
                const path = url.pathname;
                if (path.startsWith(replaceOnPrefix) && path.length > replaceOnPrefix.length)
                    mustReplace = true;
            }
            void History.navigateTo(href, mustReplace); // Internal URL
        }
    }
}

class SuppressDefaultContextMenuGesture extends Gesture {
    public static use(): void {
        debugLog?.log(`SuppressDefaultContextMenuGesture.use`);
        DocumentEvents.capturedActive.contextmenu$.subscribe((event: PointerEvent) => {
            // Suppress browser context menu anywhere except:
            // - Images
            // - Inputs (otherwise copy-paste menu gets disabled there on mobile)
            // - editor-content - this is a contentEditable div used by message editor
            const shouldStopEvent = !elementHasNameOrClass(
                event.target as HTMLElement,
                ['IMG', 'INPUT', 'editor-content']);
            if (shouldStopEvent)
                event.preventDefault();
        });
    }
}

class ContextMenuGesture extends Gesture {
    public static cancelLongPressDistance = DeviceInfo.isAndroid ? 5 : 10;
    public static defaultDelayMs = 500;

    public static use(): void {
        debugLog?.log(`ContextMenuGesture.use`);
        DocumentEvents.capturedActive.pointerDown$.subscribe((event: PointerEvent) => {
            if (event.button !== 0) // Only primary button
                return;

            const [, delayText] = getOrInheritData(event.target, 'contextMenuDelay');
            if (delayText === null && ScreenSize.isWide() && !DeviceInfo.isIos)
                return; // No 'data-context-menu-delay' + wide screen + non-iOS device: default handling

            let delay = parseInt(delayText);
            delay = isNaN(delay) ? this.defaultDelayMs : delay;
            const gesture = new ContextMenuGesture(event, delay);
            Gestures.addActive(gesture);
        });
    }

    constructor(
        public readonly startEvent: PointerEvent,
        public readonly delayMs: number
    ) {
        super();
        const startPoint = new Vector2D(startEvent.clientX, startEvent.clientY);
        this.addDisposables(
            // Events that we track
            DocumentEvents.capturedPassive.pointerMove$.subscribe((e: PointerEvent) => {
                const delta = new Vector2D(e.clientX, e.clientY).sub(startPoint).length;
                if (delta > ContextMenuGesture.cancelLongPressDistance)
                    this.dispose()
            }),
            DocumentEvents.capturedPassive.pointerUp$.subscribe(() => this.dispose()),
            DocumentEvents.capturedPassive.pointerCancel$.subscribe(() => this.dispose()),
            // We cancel it in on 'onpointerdown' handler, but it might trigger earlier on some devices
            Gestures.addActive(new SuppressEventGesture('contextmenu', 1000, true, ['INPUT', 'editor-content'])),

            // This timeout actually triggers 'contextmenu'
            new Timeout(delayMs, () => {
                // It's important to call dispose in the very beginning,
                // coz it removes 'contextmenu' suppression gesture
                this.dispose();

                let mustCancelClick = true;
                try {
                    const e = this.startEvent;
                    const event = new CustomEvent('contextmenu', {
                        bubbles: true,
                        cancelable: true,
                        detail: e.detail,
                    });
                    Object.assign(event, {
                        isCustom: true,
                        button: 1,
                        buttons: 1,
                        shiftKey: e.shiftKey,
                        ctrlKey: e.ctrlKey,
                        altKey: e.altKey,
                        metaKey: e.metaKey,
                        clientX: e.clientX,
                        clientY: e.clientY,
                        offsetX: e.offsetX,
                        offsetY: e.offsetY,
                        pageX: e.pageX,
                        pageY: e.pageY,
                        screenX: e.screenX,
                        screenY: e.screenY,
                        view: e.view,
                        relatedTarget: e.relatedTarget,
                    })
                    Object.defineProperty(event, 'target', { writable: false, value: e.target });
                    debugLog?.log(`ContextMenuGesture: triggering contextMenu event:`, event);
                    const mustHandleDefault = event.target.dispatchEvent(event);
                    mustCancelClick = event.defaultPrevented || event.cancelBubble || !mustHandleDefault;
                }
                finally {
                    const suppressContextMenuGesture = Gestures.addActive(new SuppressEventGesture('contextmenu', 300, true, ['INPUT', 'editor-content']));
                    let cancelGesture: Gesture = null;
                    const suppressGesture = Gestures.addActive(
                        new WaitForEventGesture('pointerup', (e: PointerEvent) => {
                            preventDefaultForEvent(e);
                            suppressContextMenuGesture.dispose();
                            cancelGesture?.dispose();
                            Gestures.addActive(new SuppressEventGesture('contextmenu', 300, true, ['INPUT', 'editor-content']));
                            if (mustCancelClick)
                                Gestures.addActive(new SuppressEventGesture('click', 300));
                        }, true, false));
                    cancelGesture = Gestures.addActive(
                        new WaitForEventGesture('pointercancel', () => suppressGesture.dispose()));
                }
            }),
        );
    }
}

class WaitForEventGesture extends Gesture {
    constructor(
        public readonly eventName: string,
        public readonly handler: (event: Event) => void,
        public isCapturing = true,
        public isPassive = true,
    ) {
        super();
        this.addDisposables(
            fromEvent(document, eventName, { capture: isCapturing, passive: isPassive })
                .subscribe((event: Event) => {
                    this.dispose();
                    handler(event);
                }),
        );
    }
}

class SuppressEventGesture extends Gesture {
    constructor(
        public readonly eventName: string,
        public readonly timeoutMs: number,
        public readonly justOnce: boolean = true,
        public readonly targetExclusions: string[] = null,
    ) {
        super();
        this.addDisposables(
            new Timeout(timeoutMs, () => this.dispose()),
            fromEvent(document, eventName, { capture: true, passive: false })
                .subscribe((e: Event) => {
                    const shouldStopEvent = !elementHasNameOrClass(e.target as HTMLElement, this.targetExclusions);
                    if (shouldStopEvent)
                        stopEvent(e);
                    if (justOnce)
                        this.dispose();
                }),
        );
    }
}

function elementHasNameOrClass(target: HTMLElement | null, strings: string[] | null): boolean {
    if (!target)
        return false;
    if (!strings)
        return false;
    if (strings.indexOf(target.nodeName) > -1)
        return true;
    if (strings.some(x => target.classList.contains(x)) )
        return true;
    return false;
}

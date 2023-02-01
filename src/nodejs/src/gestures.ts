import { DeviceInfo } from 'device-info';
import { Disposable } from 'disposable';
import { DocumentEvents, preventDefaultForEvent, stopEvent } from 'event-handling';
import { fromEvent, Subscription } from 'rxjs';
import { getOrInheritData } from 'dom-helpers';
import { ScreenSize } from '../../dotnet/UI.Blazor/Services/ScreenSize/screen-size';
import { Timeout } from 'timeout';
import { Vector2D } from 'math';
import { Log, LogLevel, LogScope } from 'logging';
import { FocusUI } from '../../dotnet/UI.Blazor/Services/FocusUI/focus-ui';

const LogScope: LogScope = 'Gestures';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const errorLog = Log.get(LogScope, LogLevel.Error);

export type GestureEvent = PointerEvent | MouseEvent | TouchEvent | WheelEvent;

export class Gestures {
    private static activeGestures = new Set<Gesture>();
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

        this.dumpTimeout = new Timeout(100, () => this.dumpTracked());
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

class Gesture implements Disposable {
    protected toDispose = new Array<Disposable | Subscription>();

    public static use(): void {
        return;
    }

    public dispose() {
        if (this.toDispose == null)
            return;

        const toDispose = this.toDispose;
        this.toDispose = null;
        try {
            for (const disposable of toDispose) {
                if (disposable instanceof Subscription)
                    disposable.unsubscribe();
                else
                    disposable?.dispose();
            }
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

        // We attach this event to pointerUp instead of click solely because
        // click somehow doesn't trigger on iOS for such divs, and none of
        // suggested workarounds helped.
        //
        // For issue w/ click & workarounds, see "Safari Mobile" section here:
        // - https://developer.mozilla.org/en-US/docs/Web/API/Element/click_event
        DocumentEvents.active.pointerUp$.subscribe((event: PointerEvent) => {
            debugLog?.log(`DataHrefGesture.use: pointerUp:`, event);
            if (event.button !== 0) // Only primary button
                return;

            this.tryHandle(event);
        });
    }

    public static tryHandle(event: Event) {
        if (this.blazor == null)
            return;

        const [, href] = getOrInheritData(event.target, 'href');
        if (href === null)
            return;

        // ContextMenuGesture's capturing handler may cancel this event
        if (event.defaultPrevented)
            return;

        debugLog?.log(`DataHrefGesture: navigating on data href:`, href);
        FocusUI.blur();
        this.blazor.navigateTo(href);
    }
}

class SuppressDefaultContextMenuGesture extends Gesture {
    public static use(): void {
        debugLog?.log(`SuppressDefaultContextMenuGesture.use`);
        DocumentEvents.capturedActive.contextmenu$.subscribe((event: PointerEvent) => {
            // Suppress browser context menu anywhere but on images
            const target = event.target as HTMLElement;
            if (!target || target.nodeName !== 'IMG')
                event.preventDefault();
        });
    }
}

class ContextMenuGesture extends Gesture {
    public static cancelLongPressDistance: number;
    public static defaultDelayMs = 500;

    public static use(): void {
        debugLog?.log(`ContextMenuGesture.use`);
        this.cancelLongPressDistance = DeviceInfo.isAndroid ? 5 : 10;
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
        this.toDispose.push(
            // Events that we track
            DocumentEvents.capturedPassive.pointerMove$.subscribe((e: PointerEvent) => {
                const delta = new Vector2D(e.clientX, e.clientY).sub(startPoint).length;
                if (delta > ContextMenuGesture.cancelLongPressDistance)
                    this.dispose()
            }),
            DocumentEvents.capturedPassive.pointerUp$.subscribe(() => this.dispose()),
            DocumentEvents.capturedPassive.pointerCancel$.subscribe(() => this.dispose()),
            // We cancel it in on 'onpointerdown' handler, but it might trigger earlier on some devices
            Gestures.addActive(new SuppressEventGesture('contextmenu', 1000)),

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
                    const suppressContextMenuGesture = Gestures.addActive(new SuppressEventGesture('contextmenu', 300));
                    let cancelGesture: Gesture = null;
                    const suppressGesture = Gestures.addActive(
                        new WaitForEventGesture('pointerup', (e: PointerEvent) => {
                            preventDefaultForEvent(e);
                            suppressContextMenuGesture.dispose();
                            cancelGesture?.dispose();
                            Gestures.addActive(new SuppressEventGesture('contextmenu', 300));
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
        this.toDispose.push(
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
    ) {
        super();
        this.toDispose.push(
            new Timeout(timeoutMs, () => this.dispose()),
            fromEvent(document, eventName, { capture: true, passive: false })
                .subscribe((e: Event) => {
                    stopEvent(e);
                    if (justOnce)
                        this.dispose();
                }),
        );
    }
}

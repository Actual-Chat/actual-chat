import { DeviceInfo } from 'device-info';
import { Disposable } from 'disposable';
import { DocumentEvents, endEvent } from 'event-handling';
import { fromEvent, Subscription } from 'rxjs';
import { getOrInheritData } from 'dom-helpers';
import { ScreenSize } from '../../dotnet/UI.Blazor/Services/ScreenSize/screen-size';
import { Timeout } from 'timeout';
import { Vector2D } from 'math';
import { Log, LogLevel } from 'logging';
import { FocusUI } from '../../dotnet/UI.Blazor/Services/FocusUI/focus-ui';

const LogScope = 'Gestures';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const errorLog = Log.get(LogScope, LogLevel.Error);

export type GestureEvent = PointerEvent | MouseEvent | TouchEvent | WheelEvent;

export class Gestures {
    private static activeGestures = new Set<Gesture>();
    private static dumpTimeout: Timeout | null;

    public static init(): void {
        // Used gestures
        DataHrefGesture.use();
        SuppressContextMenuGesture.use();
        ContextMenuOrDataHrefGesture.use();
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
                debugLog?.log(`- `, gesture);
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

        Gestures.removeActive(this);
        const toDispose = this.toDispose;
        this.toDispose = null;

        for (const disposable of toDispose) {
            if (disposable instanceof Subscription)
                disposable.unsubscribe();
            else
                (disposable ).dispose();
        }
    }
}

class DataHrefGesture extends Gesture {
    public static get blazor() {
        return globalThis['Blazor'] as { navigateTo: (url: string) => void };
    }

    public static use(): void {
        DocumentEvents.capturedPassive.click$.subscribe((event: PointerEvent) => {
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

        endEvent(event);
        debugLog?.log(`DataHrefGesture: navigating on data href:`, href);
        FocusUI.blur();
        this.blazor.navigateTo(href);
    }
}

class SuppressContextMenuGesture extends Gesture {
    public static use(): void {
        DocumentEvents.capturedActive.contextmenu$.subscribe((event: PointerEvent) => {
            if (event['isCustom']) // This is our custom 'contextmenu' event
                return;

            // Suppress browser context menu anywhere but on images
            const target = event.target as HTMLElement;
            if (!target || target.nodeName !== 'IMG')
                event.preventDefault();
        });
    }
}

class ContextMenuOrDataHrefGesture extends Gesture {
    public static cancelLongPressDistance: number;
    public static defaultDelayMs = 500;

    public static use(): void {
        this.cancelLongPressDistance = DeviceInfo.isAndroid ? 5 : 10;
        DocumentEvents.capturedPassive.pointerDown$.subscribe((event: PointerEvent) => {
            if (event.button !== 0) // Only primary button
                return;

            const [, delayText] = getOrInheritData(event.target, 'contextMenuDelay');
            if (delayText === null) {
                if (ScreenSize.isWide() && !DeviceInfo.isIos) {
                    DataHrefGesture.tryHandle(event);
                    return;
                }
            }

            let delay = parseInt(delayText);
            delay = isNaN(delay) ? this.defaultDelayMs : delay;
            const gesture = new ContextMenuOrDataHrefGesture(event, delay);
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
            DocumentEvents.capturedPassive.pointerMove$.subscribe((e: PointerEvent) => {
                const delta = new Vector2D(e.clientX, e.clientY).sub(startPoint).length;
                if (delta > ContextMenuOrDataHrefGesture.cancelLongPressDistance)
                    this.dispose()
            }),
            DocumentEvents.capturedPassive.pointerUp$.subscribe(() => this.dispose()),
            DocumentEvents.capturedPassive.pointerCancel$.subscribe(() => this.dispose()),
            // We should suppress default 'contextmenu' events while waiting for our own
            DocumentEvents.capturedPassive.contextmenu$.subscribe((e: MouseEvent) => {
                endEvent(e);
                this.dispose()
            }),
            // This timeout actually triggers 'contextmenu'
            new Timeout(delayMs, () => {
                this.dispose();

                let mustCancelClick = true;
                try {
                    const e = this.startEvent;
                    const event = new CustomEvent('contextmenu', {
                        bubbles: true,
                        cancelable: true,
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
                    let cancelGesture: Gesture = null;
                    const suppressGesture = Gestures.addActive(
                        new WaitForEventGesture('pointerup', () => {
                            cancelGesture?.dispose();
                            if (mustCancelClick)
                                Gestures.addActive(new SuppressEventGesture('click', 200));
                            Gestures.addActive(new SuppressEventGesture('contextmenu', 200));
                        }));
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
    ) {
        super();
        this.toDispose.push(
            fromEvent(document, eventName, { capture: true, passive: true })
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
                    endEvent(e);
                    if (justOnce)
                        this.dispose();
                }),
        );
    }
}

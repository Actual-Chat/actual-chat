import { DeviceInfo } from 'device-info';
import { DisposableBag } from 'disposable';
import { DocumentEvents, preventDefaultForEvent, stopEvent } from 'event-handling';
import { dismissSystemKeyboard } from 'keyboard';
import { fromEvent } from 'rxjs';
import { getOrInheritAttribute, getOrInheritData } from 'dom-helpers';
import { History } from '../../dotnet/UI.Blazor/Services/History/history';
import { FocusUI } from '../../dotnet/UI.Blazor/Services/FocusUI/focus-ui';
import { ScreenSize } from '../../dotnet/UI.Blazor/Services/ScreenSize/screen-size';
import { Timeout } from 'timeout';
import { Tune, TuneName, TuneUI } from '../../dotnet/UI.Blazor/Services/TuneUI/tune-ui';
import { Vector2D } from 'math';
import { getLogs } from 'logging';
import { BrowserInfo } from '../../dotnet/UI.Blazor/Services/BrowserInfo/browser-info';
import { PrefetchUI } from '../../dotnet/UI.Blazor/Services/PrefetchUI/prefetch-ui';

const { debugLog } = getLogs('Gestures');

export type GestureEvent = PointerEvent | MouseEvent | TouchEvent | WheelEvent;

export class Gestures {
    public static activeGestures = new Set<Gesture>();
    private static dumpTimeout: Timeout | null;

    public static init(): void {
        // Used gestures
        DataHrefGesture.use();
        DataPrefetchGesture.use();
        SuppressDefaultContextMenuGesture.use();
        ContextMenuGesture.use();
        DismissKeyboardOnDragGesture.use();
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
        return globalThis.Blazor as { navigateTo: (url: string) => void };
    }

    public static use(): void {
        debugLog?.log(`DataHrefGesture.use`);

        DocumentEvents.active.click$.subscribe((event: PointerEvent) => {
            if (event.button !== 0) // Only primary button
                return;

            debugLog?.log(`DataHrefGesture.use: click:`, event);
            this.tryHandle(event);
        });
    }

    private static tryHandle(event: Event): void {
        // ContextMenuGesture's capturing handler may cancel this event
        if (event.defaultPrevented)
            return;

        let [element, href] = getOrInheritData(event.target, 'href');
        const target = event.target as HTMLElement | null;
        // NOTE: workaround for target blank links on android and ios maui
        if (!href && (DeviceInfo.isIos || DeviceInfo.isAndroid) && BrowserInfo.hostKind === 'MauiApp') {
            const [anchor, aHref] = getOrInheritAttribute(target, 'href');
            if (anchor instanceof HTMLAnchorElement && anchor.target === '_blank') {
                element = anchor;
                href = aHref as string;
            }
        }
        if (href === null)
            return;

        if (target?.closest('div.pulling')) {
            // Do not trigger navigation during side-nav pulling
            return;
        }

        debugLog?.log(`DataHrefGesture: navigating on data href:`, href);
        if (!element)
            return;

        FocusUI.blur();
        const tuneName = element.dataset.hrefTune as TuneName | undefined;
        if (tuneName)
            TuneUI.play(Tune[tuneName]);
        if (href.startsWith('http://') || href.startsWith('https://'))
            location.href = href; // External URL
        else if (href.startsWith('mailto:') || href.startsWith('tel:'))
            return;
        else {
            const replaceOnPrefix = element.dataset.replaceOnPrefix;
            let mustReplace = false;
            if (replaceOnPrefix) {
                const url = new URL(location.href);
                const path = url.pathname;
                if (path.startsWith(replaceOnPrefix) && path.length > replaceOnPrefix.length) {
                    const except = element.dataset.replaceOnPrefixExcept;
                    mustReplace = !except || path !== except;
                }
            }
            History.lastClickAt = event.timeStamp;
            void History.navigateTo(href, mustReplace); // Internal URL
        }
    }
}

// Warms whatever `data-prefetch` names as soon as the pointer goes down on it, so the round trips the
// click needs are already in flight when it lands. Passive and fire-and-forget: it never affects the
// click that follows, and a pointer down that turns out to be a scroll only costs the warm-up.
class DataPrefetchGesture extends Gesture {
    public static use(): void {
        debugLog?.log(`DataPrefetchGesture.use`);

        DocumentEvents.passive.pointerDown$.subscribe((event: PointerEvent) => {
            if (event.button !== 0) // Only primary button
                return;

            const [, prefetchRef] = getOrInheritData(event.target, 'prefetch');
            if (prefetchRef === null)
                return;

            PrefetchUI.request(prefetchRef);
        });
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

            // Long-press gesture is only for touch input; mouse/touchpad users use right-click
            if (event.pointerType === 'mouse')
                return;

            const [, delayText] = getOrInheritData(event.target, 'contextMenuDelay');
            if (delayText === null && ScreenSize.isWide() && !DeviceInfo.isIos)
                return; // No 'data-context-menu-delay' + wide screen + non-iOS device: default handling

            let delay = parseInt(delayText ?? '');
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
            // Multi-touch (e.g. pinch-zoom) must cancel long-press
            DocumentEvents.capturedPassive.pointerDown$.subscribe((e: PointerEvent) => {
                if (e.pointerId !== startEvent.pointerId)
                    this.dispose();
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
                    const mustHandleDefault = event.target!.dispatchEvent(event);
                    // TODO(AY): check eslint suppressions
                    // eslint-disable-next-line @typescript-eslint/no-deprecated
                    mustCancelClick = event.defaultPrevented || event.cancelBubble || !mustHandleDefault;
                }
                finally {
                    const suppressContextMenuGesture = Gestures.addActive(new SuppressEventGesture('contextmenu', 300, true, ['INPUT', 'editor-content']));
                    let cancelGesture: Gesture | null = null;
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

class DismissKeyboardOnDragGesture extends Gesture {
    private static readonly BottomPanelSelector = '.chat-message-editor, .chat-audio-panel';

    public static use(): void {
        if (!DeviceInfo.isMobile)
            return;

        debugLog?.log(`DismissKeyboardOnDragGesture.use`);
        DocumentEvents.capturedPassive.touchMove$.subscribe((event: TouchEvent) => {
            const target = event.target;
            if (!(target instanceof Element))
                return;
            // Skip drags inside the focused editor (text selection / caret
            // repositioning) or anywhere in the bottom panel (tap-jitter on
            // attach, record, send, etc. shouldn't dismiss).
            if (document.activeElement?.contains(target))
                return;
            if (target.closest(this.BottomPanelSelector))
                return;
            dismissSystemKeyboard();
        });
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
        public readonly justOnce = true,
        public readonly targetExclusions: string[] | null = null,
    ) {
        super();
        this.addDisposables(
            new Timeout(timeoutMs, () => { this.dispose(); }),
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
    if (strings.includes(target.nodeName))
        return true;
    return strings.some(x => target.classList.contains(x));

}

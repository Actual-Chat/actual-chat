import { fromEvent, Observable } from 'rxjs';
import { Disposable } from 'disposable';
import { Log } from 'logging';
import { Timeout } from 'timeout';
import { PromiseSource, TimedOut } from 'promises';

const { debugLog, errorLog } = Log.get('event-handling');

export class EventHandler<T> implements Disposable {
    constructor(
        private readonly event: EventHandlerSet<T>,
        private readonly handler: (T) => unknown,
        private readonly justOnce: boolean = false,
    ) { }

    public dispose(): void {
        this.event.remove(this);
    }

    public trigger(argument: T): void {
        try {
            this.handler(argument);
        }
        finally {
            if (this.justOnce)
                this.dispose();
        }
    }

    public triggerSilently(argument: T): void {
        try {
            this.handler(argument);
        }
        catch (error) {
            errorLog?.log(`triggerSilently: event handler failed with an error:`, error);
            return undefined;
        }
        finally {
            if (this.justOnce)
                this.dispose();
        }
    }
}

export class EventHandlerSet<T> {
    private readonly handlers = new Set<EventHandler<T>>();

    constructor(private readonly handlersChanged?: ((handlers: Set<EventHandler<T>>) => void)) {
    }

    public get count() {
        return this.handlers.size;
    }

    public add(handler: (value: T) => unknown): EventHandler<T> {
        const eventHandler = new EventHandler<T>(this, handler, false);
        this.handlers.add(eventHandler);
        if (this.handlersChanged)
            this.handlersChanged(this.handlers);
        return eventHandler;
    }

    public addJustOnce(handler: (value: T) => unknown): EventHandler<T> {
        const eventHandler = new EventHandler<T>(this, handler, true);
        this.handlers.add(eventHandler);
        if (this.handlersChanged)
            this.handlersChanged(this.handlers);
        return eventHandler;
    }

    public remove(handler: EventHandler<T>): boolean {
        if (!this.handlers.delete(handler))
            return false;

        if (this.handlersChanged)
            this.handlersChanged(this.handlers);
        return true;
    }

    public whenNext(): Promise<T> {
        return new Promise<T>(resolve => this.addJustOnce(value => resolve(value)))
    }

    public whenNextVoid(): Promise<void> {
        return new Promise<void>(resolve => this.addJustOnce(() => resolve(undefined)))
    }

    public whenNextWithTimeout(timeoutMs: number): Promise<T | TimedOut> {
        const result = new PromiseSource<T | TimedOut>();
        let timeout: Timeout = null;
        const handler = this.addJustOnce(value => {
            timeout.clear();
            result.resolve(value)
        });
        timeout = new Timeout(timeoutMs, () => {
            handler.dispose()
            result.resolve(TimedOut.instance);
        });
        return result;
    }

    public trigger(argument: T): void {
        for (const handler of this.handlers)
            handler.trigger(argument);
    }

    public triggerSilently(argument: T): void {
        for (const handler of this.handlers)
            handler.triggerSilently(argument);
    }

    public clear(): void {
        this.handlers.clear();
    }
}

// Handy helpers

class DocumentEventSet {
    public readonly click$: Observable<MouseEvent>;
    public readonly contextmenu$: Observable<MouseEvent>;
    public readonly wheel$: Observable<WheelEvent>;
    public readonly scroll$: Observable<Event>;
    public readonly visibilityChange$: Observable<Event>;

    public readonly pointerOver$: Observable<PointerEvent>;
    public readonly pointerDown$: Observable<PointerEvent>;
    public readonly pointerMove$: Observable<PointerEvent>;
    public readonly pointerUp$: Observable<PointerEvent>;
    public readonly pointerCancel$: Observable<PointerEvent>;

    public readonly touchStart$: Observable<TouchEvent>;
    public readonly touchMove$: Observable<TouchEvent>;
    public readonly touchEnd$: Observable<TouchEvent>;
    public readonly touchCancel$: Observable<TouchEvent>;

    public readonly keyDown$: Observable<KeyboardEvent>;
    public readonly keyUp$: Observable<KeyboardEvent>;

    constructor(
        private readonly isCapture: boolean,
        private readonly isActive: boolean,
    ) {
        const document = globalThis.document;
        if (!document)
            return;

        const options = { capture: isCapture, passive: !isActive };

        this.click$ = fromEvent(document, 'click', options) as Observable<MouseEvent>;
        this.contextmenu$ = fromEvent(document, 'contextmenu', options) as Observable<MouseEvent>;
        this.wheel$ = fromEvent(document, 'wheel', options) as Observable<WheelEvent>;
        this.scroll$ = isActive ? null : fromEvent(document.defaultView, 'scroll', options);
        this.visibilityChange$ = fromEvent(document, 'visibilitychange', options);

        this.pointerOver$ = fromEvent(document, 'pointerover', options) as Observable<PointerEvent>;
        this.pointerDown$ = fromEvent(document, 'pointerdown', options) as Observable<PointerEvent>;
        this.pointerMove$ = fromEvent(document, 'pointermove', options) as Observable<PointerEvent>;
        this.pointerUp$ = fromEvent(document, 'pointerup', options) as Observable<PointerEvent>;
        this.pointerCancel$ = fromEvent(document, 'pointercancel', options) as Observable<PointerEvent>;

        this.touchStart$ = fromEvent(document, 'touchstart', options) as Observable<TouchEvent>;
        this.touchMove$ = fromEvent(document, 'touchmove', options) as Observable<TouchEvent>;
        this.touchEnd$ = fromEvent(document, 'touchend', options) as Observable<TouchEvent>;
        this.touchCancel$ = fromEvent(document, 'touchcancel', options) as Observable<TouchEvent>;

        this.keyDown$ = fromEvent(document, 'keydown', options) as Observable<KeyboardEvent>;
        this.keyUp$ = fromEvent(document, 'keyup', options) as Observable<KeyboardEvent>;
    }
}

export const DocumentEvents = {
    active: new DocumentEventSet(false, true),
    passive: new DocumentEventSet(false, false),
    capturedActive: new DocumentEventSet(true, true),
    capturedPassive: new DocumentEventSet(true, false),
}

export function stopEvent(event?: Event, stopImmediatePropagation = true, preventDefault = true) : void {
    if (!event)
        return;

    debugLog?.log(`stopEvent: event:`, event, ', stopImmediatePropagation:', stopImmediatePropagation, ', preventDefault:', preventDefault);
    event.stopPropagation();
    if (stopImmediatePropagation)
        event.stopImmediatePropagation();
    if (preventDefault)
        event.preventDefault();
}

export function preventDefaultForEvent(event?: Event) : void {
    if (!event)
        return;

    debugLog?.log(`preventDefaultForEvent: event:`, event);
    event.preventDefault();
}

export function tryPreventDefaultForEvent(event?: Event) : void {
    if (!event.defaultPrevented) {
        try {
            preventDefaultForEvent(event);
        }
        catch {
            // Intended
        }
    }
}

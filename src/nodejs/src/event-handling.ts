import { fromEvent, merge, Observable } from 'rxjs';
import { Disposable } from 'disposable';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'event-handling';
const errorLog = Log.get(LogScope, LogLevel.Error);

export class EventHandler<T> implements Disposable {
    constructor(
        private readonly event: EventHandlerSet<T>,
        private readonly handler: (T) => void,
        private readonly justOnce: boolean = false,
    ) { }

    public dispose(): void {
        this.event.remove(this);
    }

    public trigger(argument: T): void {
        try {
            return this.handler(argument);
        }
        finally {
            if (this.justOnce)
                this.dispose();
        }
    }

    public triggerSilently(argument: T): void {
        try {
            return this.handler(argument);
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

    public add(handler: (value: T) => void, justOnce = false): EventHandler<T> {
        const eventHandler = new EventHandler<T>(this, handler, justOnce);
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
        return new Promise<T>(resolve => this.add(value => resolve(value), true))
    }

    public whenNextVoid(): Promise<void> {
        return new Promise<void>(resolve => this.add(() => resolve(undefined), true))
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

    public readonly pointerOver$: Observable<PointerEvent>;
    public readonly pointerDown$: Observable<PointerEvent>;
    public readonly pointerMove$: Observable<PointerEvent>;
    public readonly pointerUp$: Observable<PointerEvent>;
    public readonly pointerCancel$: Observable<PointerEvent>;

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

        this.pointerOver$ = fromEvent(document, 'pointerover', options) as Observable<PointerEvent>;
        this.pointerDown$ = fromEvent(document, 'pointerdown', options) as Observable<PointerEvent>;
        this.pointerMove$ = fromEvent(document, 'pointermove', options) as Observable<PointerEvent>;
        this.pointerUp$ = fromEvent(document, 'pointerup', options) as Observable<PointerEvent>;
        this.pointerCancel$ = fromEvent(document, 'pointercancel', options) as Observable<PointerEvent>;

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

export function endEvent(event?: Event, stopImmediatePropagation = true, preventDefault = true) : void {
    if (!event)
        return;

    event.stopPropagation();
    if (stopImmediatePropagation)
        event.stopImmediatePropagation();
    if (preventDefault)
        event.preventDefault();
}

import { Disposable } from 'disposable';
import { Log, LogLevel } from 'logging';

const LogScope = 'event-handling';
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
    private readonly handlers = new Set<EventHandler<T>>;

    constructor(private readonly handlersChanged?: ((handlers: Set<EventHandler<T>>) => void)) {
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

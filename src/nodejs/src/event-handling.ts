import { Disposable } from 'disposable';
import { Log, LogLevel } from 'logging';
import 'logging-init';

const LogScope = 'event-handling';
const errorLog = Log.get(LogScope, LogLevel.Error);

export class EventHandler<T> implements Disposable {
    constructor(
        private readonly event: EventHandlerSet<T>,
        private readonly handler: (T) => unknown,
        private readonly justOnce: boolean = false,
    ) { }

    public dispose(): void {
        this.event.remove(this);
    }

    public trigger(argument: T) : unknown {
        try {
            return this.handler(argument);
        }
        finally {
            if (this.justOnce)
                this.dispose();
        }
    }

    public triggerSilently(argument: T) : unknown {
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
    private handlers?: Set<EventHandler<T>>;

    public add(handler: (T) => unknown, justOnce = false) : EventHandler<T> {
        const eventHandler = new EventHandler<T>(this, handler, justOnce);
        this.handlers ??= new Set<EventHandler<T>>();
        this.handlers.add(eventHandler);
        return eventHandler;
    }

    public remove(handler: EventHandler<T>) : boolean {
        return this.handlers?.delete(handler) ?? false;
    }

    public trigger(argument: T) : unknown[] | null {
        if (!this.handlers)
            return null;
        const results = new Array<unknown>();
        for (const handler of this.handlers)
            results.push(handler.trigger(argument));
        return results;
    }

    public triggerSilently(argument: T) : unknown[] | null {
        if (!this.handlers)
            return null;
        const results = new Array<unknown>();
        for (const handler of this.handlers)
            results.push(handler.triggerSilently(argument));
        return results;
    }
}

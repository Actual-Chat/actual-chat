import { Observable } from 'rxjs';
import { EventHandler, EventHandlerSet } from 'event-handling';
import { throttle } from 'promises';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'NextInteraction';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export class NextInteraction {
    private static _isInitialized = false;

    public static readonly events: EventHandlerSet<Event> = new EventHandlerSet<Event>();
    public static readonly event$ = new Observable<Event>(subject => {
        const handler = this.events.add(value => subject.next(value));
        return () => handler.dispose();
    })

    public static init(): void {
        if (this._isInitialized)
            return;

        this._isInitialized = true;
        debugLog?.log(`init`);
        const options = { capture: true, passive: true };
        document.addEventListener('click', this.onEventThrottled, options);
        document.addEventListener('doubleclick', this.onEventThrottled, options);
        document.addEventListener('onkeydown', this.onEventThrottled, options);
        document.addEventListener('touchend', this.onEventThrottled, options);
    }

    public static addHandler(handler: (Event) => unknown, justOnce = true): EventHandler<Event> {
        return this.events.add(handler, justOnce);
    }

    // Private methods

    private static readonly onEventThrottled = throttle((e: Event) => NextInteraction.onEvent(e), 200, 'skip');
    private static onEvent(event: Event): void {
        debugLog?.log(`onEvent, event:`, event);
        this.events.triggerSilently(event);
    }
}

NextInteraction.init();

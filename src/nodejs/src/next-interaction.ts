import { EventHandler, EventHandlerSet } from 'event-handling';
import { throttle } from 'promises';
import { Log, LogLevel } from 'logging';

const LogScope = 'NextInteraction';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export class NextInteraction {
    private static readonly event: EventHandlerSet<Event> = new EventHandlerSet<Event>();
    public static isStarted: boolean;

    public static start(): void {
        if (this.isStarted)
            return;

        const options = { capture: true, passive: true };
        document.addEventListener('click', this.onEventThrottled, options);
        document.addEventListener('doubleclick', this.onEventThrottled, options);
        document.addEventListener('onkeydown', this.onEventThrottled, options);
        document.addEventListener('touchend', this.onEventThrottled, options);
        this.isStarted = true;
        debugLog?.log(`start`);
    }

    public static stop(): void {
        if (!this.isStarted)
            return;

        document.removeEventListener('click', this.onEventThrottled);
        document.removeEventListener('doubleclick', this.onEventThrottled);
        document.removeEventListener('onkeydown', this.onEventThrottled);
        document.removeEventListener('touchend', this.onEventThrottled);
        this.isStarted = false;
        debugLog?.log(`stop`);
    }

    public static addHandler(handler: (Event) => unknown, justOnce = true): EventHandler<Event> {
        return this.event.add(handler, justOnce);
    }

    // Private methods

    private static readonly onEventThrottled = throttle((e: Event) => NextInteraction.onEvent(e), 200, 'skip');
    private static onEvent(event: Event): void {
        debugLog?.log(`onEvent, event:`, event);
        this.event.triggerSilently(event);
    }
}

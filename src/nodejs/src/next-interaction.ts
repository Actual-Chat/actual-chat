import { EventHandler, EventHandlerSet } from 'event-handling';
import { throttle } from 'promises';
import { Log, LogLevel } from 'logging';
import 'logging-init';

const LogScope = 'NextInteraction';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export class NextInteraction {
    private static readonly event: EventHandlerSet<Event> = new EventHandlerSet<Event>();
    private static readonly onClick = (event: Event) => NextInteraction.onEventThrottled(event);
    private static readonly onDoubleClick = (event: Event) => NextInteraction.onEventThrottled(event);
    private static readonly onKeyDown = (event: Event) => NextInteraction.onEventThrottled(event);
    private static readonly onTouchEnd = (event: Event) => NextInteraction.onEventThrottled(event);
    public static isStarted: boolean;

    public static start() : void {
        if (this.isStarted)
            return;

        const options = { passive: true };
        document.addEventListener('click', this.onClick, options);
        document.addEventListener('doubleclick', this.onDoubleClick, options);
        document.addEventListener('onkeydown', this.onKeyDown, options);
        document.addEventListener('touchend', this.onTouchEnd, options);
        this.isStarted = true;
        debugLog?.log(`start`);
    }

    public static stop() : void {
        if (!this.isStarted)
            return;

        document.removeEventListener('click', this.onClick);
        document.removeEventListener('doubleclick', this.onDoubleClick);
        document.removeEventListener('onkeydown', this.onKeyDown);
        document.removeEventListener('touchend', this.onTouchEnd);
        this.isStarted = false;
        debugLog?.log(`stop`);
    }

    public static addHandler(handler: (Event) => unknown, justOnce = true) : EventHandler<Event> {
        return this.event.add(handler, justOnce);
    }

    // Private methods

    private static readonly onEventThrottled = throttle((e: Event) => NextInteraction.onEvent(e), 500, 'skip');
    private static onEvent(event: Event) : void {
        debugLog?.log(`onEvent, event:`, event);
        this.event.triggerSilently(event);
    }
}

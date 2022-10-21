import { EventHandler, EventHandlerSet } from 'event-handling';
import { debounce } from 'debounce';

const LogScope = 'NextInteraction';
const debug = true;

export class NextInteraction {
    private static readonly event: EventHandlerSet<Event> = new EventHandlerSet<Event>();
    private static readonly onEventDebounced = debounce((e: Event) => NextInteraction.onEvent(e), 500, true);
    private static readonly onClick = (event: Event) => NextInteraction.onEventDebounced(event);
    private static readonly onDoubleClick = (event: Event) => NextInteraction.onEventDebounced(event);
    private static readonly onKeyDown = (event: Event) => NextInteraction.onEventDebounced(event);
    private static readonly onTouchEnd = (event: Event) => NextInteraction.onEventDebounced(event);
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

        if (debug)
            console.debug(`${LogScope}.start()`);
    }

    public static stop() : void {
        if (!this.isStarted)
            return;

        document.removeEventListener('click', this.onClick);
        document.removeEventListener('doubleclick', this.onDoubleClick);
        document.removeEventListener('onkeydown', this.onKeyDown);
        document.removeEventListener('touchend', this.onTouchEnd);
        this.isStarted = false;

        if (debug)
            console.debug(`${LogScope}.stop()`);
    }

    public static addHandler(handler: (Event) => unknown, justOnce = true) : EventHandler<Event> {
        return this.event.add(handler, justOnce);
    }

    // Private methods

    private static onEvent(event: Event) : void {
        if (debug)
            console.debug(`${LogScope}.onEvent(), event:`, event);
        this.event.triggerSilently(event);
    }
}

import { Observable } from 'rxjs';
import { ResolvedPromise } from 'promises';
import { EventHandlerSet } from 'event-handling';
import { Log } from 'logging';

const { debugLog } = Log.get('Interactive');

export class Interactive {
    private static _isInitialized = false;
    private static _isInteractive = false;
    private static _isAlwaysInteractive = false;

    public static interactionEvents: EventHandlerSet<Event>;
    public static readonly interactionEvent$ = new Observable<Event>(subject => {
        const handler = this.interactionEvents.add((event: Event) => subject.next(event));
        return () => handler.dispose();
    })
    public static readonly isInteractiveChanged = new EventHandlerSet<boolean>();
    public static readonly isInteractiveChange$ = new Observable<boolean>(subject => {
        const handler = this.isInteractiveChanged.add(value => subject.next(value));
        return () => handler.dispose();
    })

    public static init() {
        if (this._isInitialized)
            return;

        this._isInitialized = true;
        debugLog?.log(`init`);

        // Initialize interactionEvents
        const onInteractionEvents = ['touchend', 'click'];
        let onInteractionHandlersAttached = false;
        this.interactionEvents = new EventHandlerSet<Event>(handlers => {
            const requiresOnInteractionHandlers = handlers.size != 0;
            if (requiresOnInteractionHandlers && !onInteractionHandlersAttached) {
                const options = { capture: true, passive: true };
                onInteractionEvents.forEach((e) => document.body.addEventListener(e, this.onInteractionEvent, options));
            }
            else if (onInteractionHandlersAttached && !requiresOnInteractionHandlers) {
                const options = { capture: true, passive: true };
                onInteractionEvents.forEach((e) => document.body.removeEventListener(e, this.onInteractionEvent, options));
            }
            onInteractionHandlersAttached = requiresOnInteractionHandlers;
        });
    }

    public static get isInteractive(): boolean {
        return this._isInteractive;
    }
    public static set isInteractive(value: boolean) {
        if (this._isAlwaysInteractive)
            value = true;
        if (this._isInteractive == value)
            return;

        debugLog?.log(`isInteractive:`, value);
        this._isInteractive = value;
        this.isInteractiveChanged.triggerSilently(value);
    }

    public static get isAlwaysInteractive() {
        return this._isAlwaysInteractive;
    }
    public static set isAlwaysInteractive(value: boolean) {
        if (this._isAlwaysInteractive == value)
            return;

        debugLog?.log(`isAlwaysInteractive:`, value);
        this._isAlwaysInteractive = value;
        this.isInteractive = value;
    }

    public static whenInteractive(): Promise<void> {
        return this._isInteractive
            ? ResolvedPromise.Void
            : this.isInteractiveChanged.whenNextVoid();
    }

    // Private methods

    private static onInteractionEvent = (event: Event) => {
        if (!event.isTrusted)
            return; // not an user action - e.g., triggered by JS dispatchEvent

        this.interactionEvents.triggerSilently(event)
    };
}

Interactive.init();
globalThis['Interactive'] = Interactive;

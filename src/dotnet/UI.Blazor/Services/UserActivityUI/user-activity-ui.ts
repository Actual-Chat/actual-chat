import { DocumentEvents } from 'event-handling';
import { delayAsync, throttle } from "promises";
import { Log } from 'logging';

const { debugLog } = Log.get('UserActivityUI');

const PostInteractionActivityPeriodMs = 30_000;

export class UserActivityUI {
    private static _blazorRef: DotNet.DotNetObject;
    private static _activityPeriodMs: number;
    private static _activeUntil: number = Date.now() + PostInteractionActivityPeriodMs;
    private static notifyBackendThrottled: () => void;

    public static get activeUntil() { return this._activeUntil; }

    public static init(blazorRef: DotNet.DotNetObject, activityPeriodMs: number, notifyPeriodMs: number) {
        this._blazorRef = blazorRef;
        this._activityPeriodMs = activityPeriodMs;
        this.notifyBackendThrottled = throttle(() => this.notifyBackend(), notifyPeriodMs);

        const documentEvents = DocumentEvents.passive;
        documentEvents.visibilityChange$.subscribe(_ => {
            if (!document.hidden)
                this.onInteraction();
            else
                this.onInteraction(0, true);
        })
        documentEvents.pointerMove$.subscribe(_ => this.onInteraction());
        documentEvents.pointerDown$.subscribe(_ => this.onInteraction());
        documentEvents.keyDown$.subscribe(_ => this.onInteraction());

        (async () => {
            await delayAsync(1000);
            this.onInteraction();
        })();
    }

    private static onInteraction(activityPeriodMs?: number, force = false): void {
        activityPeriodMs ??= this._activityPeriodMs;
        const newActiveUntil = Date.now() + activityPeriodMs;
        if (!force && this._activeUntil > newActiveUntil)
            return;

        this._activeUntil = newActiveUntil;
        this.notifyBackendThrottled();
    }

    private static notifyBackend = async () => {
        const willBeActiveForMs = this._activeUntil - Date.now();
        if (willBeActiveForMs <= 0)
            return;

        debugLog?.log(`notifyBackend`);
        await this._blazorRef.invokeMethodAsync('OnInteraction', willBeActiveForMs);
    }
}

import { DocumentEvents } from 'event-handling';
import { delayAsync, throttle } from 'actuallab-core';
import { getLogs } from 'logging';

const { debugLog } = getLogs('UserActivityUI');

const PostInteractionActivityPeriodMs = 30_000;

export class UserActivityUI {
    private static _blazorRef: DotNet.DotNetObject;
    private static _activityPeriodMs: number;
    private static _activeUntil: number = performance.now() + PostInteractionActivityPeriodMs;
    private static notifyBackendThrottled: () => void;

    public static get activeUntil() { return this._activeUntil; }

    public static init(blazorRef: DotNet.DotNetObject, activityPeriodMs: number, notifyPeriodMs: number) {
        this._blazorRef = blazorRef;
        this._activityPeriodMs = activityPeriodMs;
        this.notifyBackendThrottled = throttle(() => this.notifyBackend(), notifyPeriodMs);

        const documentEvents = DocumentEvents.passive;
        documentEvents.visibilityChange$.subscribe(() => {
            if (!document.hidden)
                this.onInteraction();
            else {
                // Bypasses the throttle - the hidden transition must reach .NET right away,
                // otherwise presence lingers for up to notifyPeriodMs after the tab is gone.
                this.onInteraction(0, true);
                void this.notifyBackend();
            }
        })
        documentEvents.pointerMove$.subscribe(() => this.onInteraction());
        documentEvents.pointerDown$.subscribe(() => this.onInteraction());
        documentEvents.keyDown$.subscribe(() => this.onInteraction());

        void (async () => {
            await delayAsync(1000);
            this.onInteraction();
        })();
    }

    private static onInteraction(activityPeriodMs?: number, force = false): void {
        activityPeriodMs ??= this._activityPeriodMs;
        const newActiveUntil = performance.now() + activityPeriodMs;
        if (!force && this._activeUntil > newActiveUntil)
            return;

        this._activeUntil = newActiveUntil;
        this.notifyBackendThrottled();
    }

    private static notifyBackend = async () => {
        // Zero is meaningful here: it's how .NET learns the user is no longer present
        const willBeActiveForMs = Math.max(0, this._activeUntil - performance.now());
        debugLog?.log(`notifyBackend`);
        await this._blazorRef.invokeMethodAsync('OnInteraction', willBeActiveForMs);
    }
}

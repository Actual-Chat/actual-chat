import { EventHandlerSet } from 'event-handling';

/** Connectivity state propagated from the main thread via RPC */
export class WorkerConnectivityUI {
    private static _isOnline = true;
    private static _isConnected = true;
    private static _lastCameOnlineAt: number | null = null;

    public static readonly isOnlineChanged = new EventHandlerSet<boolean>();

    public static get isOnline(): boolean { return this._isOnline; }
    public static get isConnected(): boolean { return this._isConnected; }

    public static update(isOnline: boolean, isConnected: boolean): void {
        const wasOnline = this._isOnline;
        this._isOnline = isOnline;
        this._isConnected = isConnected;

        if (isOnline && !wasOnline) {
            this._lastCameOnlineAt = Date.now();
            this.isOnlineChanged.triggerSilently(true);
        } else if (!isOnline && wasOnline) {
            this.isOnlineChanged.triggerSilently(false);
        }
    }

    public static justBecameOnline(recency = 1000): boolean {
        if (!this._isOnline || this._lastCameOnlineAt == null)
            return false;
        return (Date.now() - this._lastCameOnlineAt) <= recency;
    }
}

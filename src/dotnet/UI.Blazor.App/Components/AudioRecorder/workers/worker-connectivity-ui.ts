import { EventHandlerSet } from 'event-handling';
import { PromiseSource } from 'actuallab-core';

/** Connectivity state propagated from the main thread via RPC */
export class WorkerConnectivityUI {
    private static _isOnline = true;
    private static _isConnected = true;
    private static _isBlazorServer = false;
    private static _lastCameOnlineAt: number | null = null;
    private static _lastCameConnectedAt: number | null = null;

    public static get isOnline(): boolean { return this._isOnline; }
    public static get isConnected(): boolean { return this._isConnected; }
    public static get isBlazorServer(): boolean { return this._isBlazorServer; }

    public static readonly isOnlineChanged = new EventHandlerSet<boolean>();
    public static readonly isConnectedChanged = new EventHandlerSet<boolean>();
    public static readonly whenReady = new PromiseSource<void>();

    public static update(isOnline: boolean, isConnected: boolean, isBlazorServer: boolean): void {
        this._isBlazorServer = isBlazorServer;
        const wasOnline = this._isOnline;
        const wasConnected = this._isConnected;
        this._isOnline = isOnline;
        this._isConnected = isConnected;

        if (isOnline && !wasOnline)
            this._lastCameOnlineAt = Date.now();
        if (isConnected && !wasConnected)
            this._lastCameConnectedAt = Date.now();
        if (!this.whenReady.isCompleted)
            this.whenReady.resolve();

        if (wasOnline !== isOnline)
            this.isOnlineChanged.triggerSilently(isOnline);
        if (wasConnected !== isConnected)
            this.isConnectedChanged.triggerSilently(isConnected);
    }

    public static justBecameOnline(recency = 1000): boolean {
        if (!this._isOnline || this._lastCameOnlineAt == null)
            return false;

        return (Date.now() - this._lastCameOnlineAt) <= recency;
    }

    public static justBecameConnected(recency = 1000): boolean {
        if (!this._isConnected || this._lastCameConnectedAt == null)
            return false;

        return (Date.now() - this._lastCameConnectedAt) <= recency;
    }
}

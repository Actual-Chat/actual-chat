import { EventHandlerSet } from 'event-handling';
import { delayAsync, PromiseSource, PromiseSourceWithTimeout } from 'actuallab-core';
import { getLogs } from 'logging';

const { infoLog, warnLog, errorLog } = getLogs('ConnectivityUI');

export class ConnectivityUI {
    private static _isOnline = true;
    private static _isConnected = true;
    private static _isBlazorServer = false;
    private static _lastCameOnlineAt: number | null = null;
    private static _lastCameConnectedAt: number | null = null;
    private static _backendRef: DotNet.DotNetObject | null = null;

    public static get isOnline(): boolean { return this._isOnline; }
    public static get isConnected(): boolean { return this._isConnected; }
    public static get isBlazorServer(): boolean { return this._isBlazorServer; }

    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    public static readonly isMauiApp = globalThis.document?.body.classList.contains('app-maui') ?? false;
    public static readonly isOnlineChanged = new EventHandlerSet<boolean>();
    public static readonly isConnectedChanged = new EventHandlerSet<boolean>();
    public static readonly whenReady = new PromiseSource<void>();

    /** Called from C# to initialize connectivity tracking */
    public static init(backendRef: DotNet.DotNetObject | null, isBlazorServer: boolean): void {
        this._backendRef = backendRef;
        this._isBlazorServer = isBlazorServer;

        if (!this.isMauiApp) { // MauiApp uses MauiConnectivityUI to report online/offline state
            const setOnline = (isOnline: boolean) => {
                this.setOnline(isOnline);
                if (this._isBlazorServer)
                    this.setConnected(isOnline);
            };
            try {
                globalThis.addEventListener('online', () => setOnline(true));
                globalThis.addEventListener('offline', () => setOnline(false));
            } catch { /* ignore if not available */ }
        }
        if (!this.whenReady.isCompleted)
            this.whenReady.resolve();
    }

    /** Called from C# MauiConnectivityUI to push platform connectivity state */
    public static setOnline(isOnline: boolean | null = null): void {
        const wasOnline = this._isOnline;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        this._isOnline = isOnline ??= navigator?.onLine ?? true;

        if (isOnline && !wasOnline)
            this._lastCameOnlineAt = Date.now();

        if (wasOnline !== isOnline) {
            infoLog?.log(`isOnline: ${isOnline}`);
            this.isOnlineChanged.triggerSilently(isOnline);
            void this.notifyBackend(isOnline);
        }
    }

    /** Called from C# to push platform connectivity state */
    public static setConnected(isConnected: boolean): void {
        const wasConnected = this._isConnected;
        this._isConnected = isConnected;

        if (isConnected && !wasConnected)
            this._lastCameConnectedAt = Date.now();

        if (wasConnected !== isConnected) {
            infoLog?.log(`isConnected: ${isConnected}`);
            this.isConnectedChanged.triggerSilently(isConnected);
        }
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

    public static async whenOnline(maxWaitMs?: number): Promise<boolean> {
        if (this.isOnline)
            return true;

        const result = new PromiseSourceWithTimeout<boolean>();
        if (maxWaitMs !== undefined)
            result.setTimeout(maxWaitMs, () => result.resolve(false));
        const handler = this.isOnlineChanged.add(isOnline => {
            if (isOnline)
                result.resolve(true);
        })
        try {
            return await result;
        }
        finally {
            handler.dispose();
        }
    }

    public static async whenConnected(maxWaitMs?: number): Promise<boolean> {
        if (this.isConnected)
            return true;

        const result = new PromiseSourceWithTimeout<boolean>();
        if (maxWaitMs !== undefined)
            result.setTimeout(maxWaitMs, () => result.resolve(false));
        const handler = this.isConnectedChanged.add(isConnected => {
            if (isConnected)
                result.resolve(true);
        })
        try {
            return await result;
        }
        finally {
            handler.dispose();
        }
    }

    public static async whenReadyToReload(reason: string): Promise<boolean> {
        if (this.isMauiApp)
            return true; // MAUI app is always ready to reload

        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        while (true) {
            if (!await this.whenOnline(10_000)) {
                warnLog?.log(`whenReadyToReload('${reason}'): not online yet...`);
                continue;
            }
            if (await check() && await check(50))
                return true;

            warnLog?.log(`whenReadyToReload('${reason}'): online, but can't reach the server...`);
            await delayAsync(1000);
        }

        async function check(delayMs = 0): Promise<boolean> {
            if (delayMs > 0)
                await delayAsync(delayMs);
            try {
                const response = await fetch('/favicon_voxt.ico', { cache: 'no-store' });
                if (response.ok)
                    return true;
            }
            catch {
                // Intended
            }
            return false;
        }
    }

    // Private methods

    private static async notifyBackend(isOnline: boolean): Promise<void> {
        if (!this._backendRef)
            return;

        try {
            await this._backendRef.invokeMethodAsync('OnOnlineChanged', isOnline);
        } catch (e) {
            errorLog?.log('notifyBackend: failed', e);
        }
    }
}

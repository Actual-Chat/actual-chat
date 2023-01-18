import { Observable } from 'rxjs';
import { delayAsync, ResolvedPromise } from 'promises';
import { EventHandlerSet } from 'event-handling';
import { NextInteraction } from 'next-interaction';
import { Log, LogLevel } from 'logging';

const LogScope = 'InteractiveUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class InteractiveUI {
    private static backendRef: DotNet.DotNetObject = null;
    private static _isInteractive = false;
    private static _backendIsInteractive = false;
    private static _isAlwaysInteractive = false;

    public static readonly isInteractiveChanged = new EventHandlerSet<boolean>();
    public static readonly isInteractiveChange$ = new Observable<boolean>(subject => {
        const handler = this.isInteractiveChanged.add(value => subject.next(value));
        return () => handler.dispose();
    })

    public static init(backendRef: DotNet.DotNetObject) {
        debugLog?.log(`init`);
        this.backendRef = backendRef;
        NextInteraction.addHandler(() => this.isInteractive = true, false);
    }

    public static get isInteractive(): boolean {
        return this._isInteractive;
    }
    public static set isInteractive(value: boolean) {
        if (this._isAlwaysInteractive)
            value = true;
        if (this._isInteractive == value)
            return;

        this._isInteractive = value;
        this.isInteractiveChanged.triggerSilently(value);
        this.trySync();
    }

    public static get isAlwaysInteractive() {
        return this._isAlwaysInteractive;
    }
    public static set isAlwaysInteractive(value: boolean) {
        if (this._isAlwaysInteractive == value)
            return;

        this._isAlwaysInteractive = value;
        this.isInteractive = value;
    }

    public static whenInteractive(): Promise<void> {
        return this._isInteractive
               ? ResolvedPromise.Void
               : this.isInteractiveChanged.whenNextVoid();
    }

    // Private methods

    private static trySync(): void {
        if (this._isInteractive != this._backendIsInteractive)
            void this.sync();
    }

    private static _isSyncing: boolean;
    private static async sync(): Promise<void> {
        if (this._isSyncing)
            return; // Running sync will do the job anyway - it loops while there is any diff

        this._isSyncing = true;
        try {
            while (this._isInteractive != this._backendIsInteractive) {
                try {
                    debugLog?.log(`sync: calling IsInteractiveChanged(${this._isInteractive}) on backend`);
                    this._backendIsInteractive = await this.backendRef
                        .invokeMethodAsync("IsInteractiveChanged", this._isInteractive);
                }
                catch (error) {
                    errorLog?.log(`sync: failed to reach the backend, error:`, error);
                    await delayAsync(1000);
                }
            }
        }
        finally {
            this._isSyncing = true;
        }
    }
}

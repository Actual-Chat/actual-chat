import { Observable } from 'rxjs';
import { ResolvedPromise } from 'promises';
import { EventHandlerSet } from 'event-handling';
import { NextInteraction } from 'next-interaction';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'Interactive';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class Interactive {
    private static _isInitialized = false;
    private static _isInteractive = false;
    private static _isAlwaysInteractive = false;

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
}

Interactive.init();

import { NextInteraction } from 'next-interaction';
import { EventHandler } from 'event-handling';
import { Log, LogLevel } from 'logging';

const LogScope = 'UserActivityUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export class UserActivityUI {
    private _blazorRef: DotNet.DotNetObject;
    private readonly _handler: EventHandler<Event>;
    private _lastActiveAt: Date = new Date();
    private _shouldNotify: boolean;
    public static create(blazorRef: DotNet.DotNetObject) {
        return new UserActivityUI(blazorRef);
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        this._blazorRef = blazorRef;
        this._handler = NextInteraction.addHandler(() => this.onInteracted(), false)
    }

    public dispose() {
        this._handler.dispose();
    }

    public getLastActiveAt() {
        return this._lastActiveAt;
    }

    public subscribeForNext() {
        this._shouldNotify = true;
    }

    private async onInteracted() {
        debugLog?.log(`onInteracted: user interaction happened`);
        this._lastActiveAt = new Date();
        if (this._shouldNotify) {
            debugLog?.log(`onInteracted: notifying server about user activity`);
            await this._blazorRef.invokeMethodAsync('OnInteracted');
            this._shouldNotify = false;
        }
    }
}

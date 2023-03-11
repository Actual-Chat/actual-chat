import { EventHandler } from 'event-handling';
import { Interactive } from 'interactive';
import { Log } from 'logging';

const { debugLog } = Log.get('UserActivityUI');

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
        this._handler = Interactive.interactionEvents.add(() => this.onInteracted())
    }

    public dispose() {
        this._handler.dispose();
    }

    public getLastActiveAt() : Date {
        return this._lastActiveAt;
    }

    public subscribeForNext() {
        this._shouldNotify = true;
    }

    private async onInteracted() {
        debugLog?.log(`onInteracted: user interaction happened`);
        this._lastActiveAt = new Date();
        if (!this._shouldNotify)
            return;

        this._shouldNotify = false;
        debugLog?.log(`onInteracted: notifying server about user activity`);
        await this._blazorRef.invokeMethodAsync('OnInteracted');
    }
}

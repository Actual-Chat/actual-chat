import { Disposable } from 'disposable';
import { AudioContextSource } from 'audio-context-source';
import { EventHandler, EventHandlerSet } from 'event-handling';
import { PromiseSource } from 'promises';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AudioContextRef';
const debugLog = Log.get(LogScope, LogLevel.Debug);

let nextId = 0;

export class AudioContextRef implements Disposable {
    private readonly _id: number;
    private readonly _changedHandler: EventHandler<AudioContext | null>;
    private _whenContextChanged: PromiseSource<AudioContext | null>;
    private _isDisposed = false;

    public readonly contextChanged = new EventHandlerSet<AudioContext | null>();

    constructor(
        public source: AudioContextSource,
        public context: AudioContext
    ) {
        this._id = ++nextId;
        debugLog?.log(`constructor(#${this._id}): source:`, source, ', context:', context);
        this._whenContextChanged = new PromiseSource<AudioContext>();
        this._changedHandler = source.changedEvents.add(context => {
            debugLog?.log(`contextChanged(#${this._id}): new context:`, context);
            this.context = context;
            const whenContextChanged = this._whenContextChanged;
            this._whenContextChanged = new PromiseSource<AudioContext>();
            whenContextChanged.resolve(context);
            this.contextChanged.triggerSilently(context);
        });
    }

    public dispose(): void {
        if (this._isDisposed)
            return;

        debugLog?.log(`dispose(#${this._id})`);
        this._isDisposed = true;
        this._changedHandler.dispose();

        // Make sure our very last event "delivers" null AudioContext
        if (this._whenContextChanged.isCompleted())
            this._whenContextChanged = new PromiseSource<AudioContext>();
        this._whenContextChanged.resolve(null);
        this.contextChanged.triggerSilently(null);
    }

    public async whenContextChanged(): Promise<AudioContext> {
        return this._whenContextChanged;
    }
}

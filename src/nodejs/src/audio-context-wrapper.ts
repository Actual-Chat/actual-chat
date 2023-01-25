import { Disposable } from 'disposable';
import { AudioContextSource } from 'audio-context-source';
import { EventHandler } from 'event-handling';
import { PromiseSource } from 'promises';

export class AudioContextWrapper implements Disposable {
    private readonly _refreshHandler: EventHandler<AudioContext>;
    private _whenRefreshed: PromiseSource<void>;

    constructor(public source: AudioContextSource, public context: AudioContext) {
        this._refreshHandler = source.audioContextChangedEvents.add(c => this.refreshContext(c));
        this._whenRefreshed = new PromiseSource<void>();
    }

    public async whenContextRefreshed(): Promise<AudioContext> {
        await this._whenRefreshed;
        this._whenRefreshed = new PromiseSource<void>();
        return this.context;
    }

    private refreshContext(context: AudioContext): void {
        this.context = context;
        this._whenRefreshed.resolve(undefined);
    }

    public dispose(): void {
        this._refreshHandler.dispose();
        this._whenRefreshed.reject(undefined);
        this.source.release(this);
    }
}

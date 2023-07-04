import { AsyncDisposable } from 'disposable';
import {
    cancelled,
    Cancelled,
    OperationCancelledError,
    PromiseSource,
    waitAsync,
} from 'promises';
import { nextTickAsync } from 'timeout';
import { AudioContextSource } from './audio-context-source';
import { Log } from 'logging';

const { debugLog, errorLog } = Log.get('AudioContextRef');

let nextId = 1;

export interface AudioContextRefOptions {
    attach?: (context: AudioContext) => Promise<void> | void,
    detach?: (context: AudioContext) => Promise<void> | void,
    dispose?: () => Promise<void> | void,
    ready?: (context: AudioContext) => Promise<void> | void,
    unready?: (context: AudioContext) => Promise<void> | void,
    retryCount?: number,
}

export class AudioContextRef implements AsyncDisposable {
    private readonly name: string;
    private readonly _whenFirstTimeReady = new PromiseSource<AudioContext>;
    private readonly _whenRunning: Promise<void>;
    private readonly _whenDisposeRequested = new PromiseSource<Cancelled>;
    private _context: AudioContext;

    public get context() { return this._context }

    constructor(
        public readonly source: AudioContextSource,
        public readonly operationName: string,
        private readonly options: AudioContextRefOptions,
    ) {
        this.name = `#${nextId++}-${operationName}`
        this._whenRunning = this.maintain();
    }

    async disposeAsync() : Promise<void> {
        debugLog?.log(`${this.name}.disposeAsync`);
        if (!this._whenDisposeRequested.isCompleted())
            this._whenDisposeRequested.resolve(cancelled)

        await this._whenRunning;
    }

    public whenFirstTimeReady() {
        return waitAsync(this._whenFirstTimeReady, this._whenDisposeRequested);
    }

    public async whenDisposed() {
        await this._whenRunning;
    }

    private async maintain(): Promise<void> {
        // This method should never throw!

        // We want to do the rest after completion of post-AudioContextSource.getRef logic
        await nextTickAsync();

        let lastContext: AudioContext = null;
        try {
            while (!this._whenDisposeRequested.isCompleted()) {
                debugLog?.log(`${this.name}: awaiting whenReady`);
                this._context = await this.source.whenReady(this._whenDisposeRequested);
                if (lastContext === this.context) {
                    debugLog?.log(`${this.name}: ready, context:`, Log.ref(this._context));
                    await this.options.ready?.(this._context);
                }
                else {
                    if (lastContext) {
                        debugLog?.log(`${this.name}: detach, context:`, Log.ref(lastContext));
                        await this.options.detach?.(lastContext);
                        lastContext = null;
                    }
                    debugLog?.log(`${this.name}: attach, context:`, Log.ref(this._context));
                    await this.options.attach?.(this._context);
                    lastContext = this._context;
                    this._whenFirstTimeReady.resolve(this._context);
                }

                debugLog?.log(`${this.name}: awaiting whenNotReady`);
                await this.source.whenNotReady(this._context, this._whenDisposeRequested);
                debugLog?.log(`${this.name}: unready, context:`, Log.ref(this._context));
                await this.options.unready?.(this._context);
            }
        }
        catch (e) {
            const mustReport = !((e instanceof OperationCancelledError) && this._whenDisposeRequested.isCompleted());
            if (mustReport)
                errorLog?.log(`${this.name}.maintain: error:`, e);
        }

        debugLog?.log(`${this.name}.maintain: shutting down...`);
        if (!this._whenDisposeRequested.isCompleted())
            this._whenDisposeRequested.resolve(cancelled); // Just for the consistency

        try {
            if (lastContext) {
                debugLog?.log(`${this.name}: final detach, context:`, Log.ref(lastContext));
                await this.options.detach?.(lastContext);
                lastContext = null;
            }
        }
        catch (e) {
            if (!(e instanceof OperationCancelledError))
                errorLog?.log(`${this.name}.maintain: final detach failed:`, e);
        }

        try {
            await this.options.dispose?.();
        }
        catch (e) {
            errorLog?.log(`${this.name}.maintain: dispose handler failed:`, e);
        }
    }
}

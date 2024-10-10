import { AsyncDisposable, Disposable, Disposables } from 'disposable';
import {
    cancelled,
    Cancelled,
    OperationCancelledError,
    PromiseSource,
    waitAsync,
} from 'promises';
import { nextTickAsync } from 'timeout';
import { firstValueFrom } from 'rxjs';
import { AudioContextSource, OverridenAudioContext } from './audio-context-source';
import { Log } from 'logging';

const { debugLog, errorLog } = Log.get('AudioContextRef');

let nextId = 1;

export interface AudioContextRefOptions {
    attach?: (context: OverridenAudioContext) => Promise<void> | void,
    detach?: (context: OverridenAudioContext) => Promise<void> | void,
    dispose?: () => Promise<void> | void,
    retryCount?: number,
}

export class AudioContextRef implements AsyncDisposable {
    private readonly name: string;
    private readonly whenRunning: Promise<void>;
    private readonly whenDisposeRequested = new PromiseSource<Cancelled>();
    private _whenReady = new PromiseSource<OverridenAudioContext>();
    private context?: OverridenAudioContext = null;
    private inUse?: Disposable = null;

    public get isUsed(): boolean { return this.inUse != null; }

    constructor(
        public readonly source: AudioContextSource,
        public readonly operationName: string,
        private readonly options: AudioContextRefOptions,
    ) {
        this.name = `#${nextId++}-${operationName}`
        this.whenRunning = this.maintain();
    }

    async disposeAsync() : Promise<void> {
        debugLog?.log(`${this.name}.disposeAsync`);
        this.inUse?.dispose();
        this.inUse = null;

        if (!this.whenDisposeRequested.isCompleted())
            this.whenDisposeRequested.resolve(cancelled)

        await this.whenRunning;
    }

    public async whenDisposed(): Promise<void> {
        await this.whenRunning;
    }

    public use(whenContextInUse: (audioContext: OverridenAudioContext) => Promise<void>): Disposable {
        const dispose = () => {
            debugLog?.log(`${this.name}.use.dispose()`);
            if (!this.isUsed)
                return;

            this.inUse?.dispose();
            this.inUse = null;
        };
        waitAsync(this._whenReady, this.whenDisposeRequested)
            .then(context => {
                this.inUse = this.source.useRef(this);
                return waitAsync(whenContextInUse(context), this.whenDisposeRequested);
            })
            .catch(e => {
                errorLog?.log(`${this.name}.use: error:`, e);
                dispose();
            });
        return {
            dispose: dispose,
        };
    }

    private async maintain(): Promise<void> {
        // This method should never throw!

        // We want to do the rest after completion of post-AudioContextSource.getRef logic
        await nextTickAsync();

        let lastContext: AudioContext = null;

        while (!this.whenDisposeRequested.isCompleted()) {
            try {
                debugLog?.log(`${this.name}: awaiting whenReady`);
                const whenContextClosed = firstValueFrom(this.source.contextClosed$);
                const context = await Promise.race([this.source.whenReady(this.whenDisposeRequested), whenContextClosed]);
                if (context.state === 'closed') {
                    if (this.context === context) {
                        debugLog?.log(`${this.name}: detach, context:`, Log.ref(context));
                        this.context = null;
                        lastContext = null;
                        await this.options.detach?.(context);
                    }
                    continue;
                }

                this.context = context;
                if (lastContext !== this.context) {
                    if (lastContext) {
                        debugLog?.log(`${this.name}: detach, context:`, Log.ref(lastContext));
                        await this.options.detach?.(lastContext);
                        lastContext = null;
                    }
                    debugLog?.log(`${this.name}: attach, context:`, Log.ref(this.context));
                    await this.options.attach?.(this.context);
                    lastContext = this.context;
                    this._whenReady.resolve(this.context);
                }

                debugLog?.log(`${this.name}: awaiting whenNotReady`);
                await this.source.whenNotReady(this.context, this.whenDisposeRequested);
                this._whenReady = new PromiseSource<OverridenAudioContext>();
                if (this.context.state === 'closed')
                    await this.options.detach?.(this.context);
            }
            catch (e) {
                const isDisposeRequested = ((e instanceof OperationCancelledError) && this.whenDisposeRequested.isCompleted());
                if (!isDisposeRequested) {
                    errorLog?.log(`${this.name}.maintain: error:`, e);
                    await this.options.detach?.(lastContext);
                    lastContext = null;
                }
            }
        }

        debugLog?.log(`${this.name}.maintain: shutting down...`);

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

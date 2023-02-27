import { AsyncDisposable } from 'disposable';
import {
    cancelled,
    Cancelled,
    OperationCancelledError,
    PromiseSource,
    ResolvedPromise,
    retryAsync,
    waitAsync,
} from 'promises';
import { nextTickAsync } from 'timeout';
import { AudioContextSource } from 'audio-context-source';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AudioContextRef';
const debugLog = Log.get(LogScope, LogLevel.Debug);
// eslint-disable-next-line @typescript-eslint/no-unused-vars
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

let nextId = 1;

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
        private readonly attach?: (context: AudioContext) => Promise<void> | void,
        private readonly detach?: (context: AudioContext) => Promise<void> | void,
        private readonly ready?: (context: AudioContext) => Promise<void> | void,
        private readonly unready?: (context: AudioContext) => Promise<void> | void,
    ) {
        this.name = `#${nextId++}-${operationName}`
        this._whenRunning = this.run();
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

    private async run(): Promise<void> {
        // This method should never throw!

        // We want to run the rest after completion of post-AudioContextSource.getRef logic
        await nextTickAsync();
        let lastContext: AudioContext = null;

        // noinspection InfiniteLoopJS
        while (!this._whenDisposeRequested.isCompleted()) {
            try {
                debugLog?.log(`${this.name}: awaiting whenReady`);
                this._context = await this.source.whenReady(this._whenDisposeRequested);
                if (lastContext === this.context) {
                    debugLog?.log(`${this.name}: ready, context:`, this._context);
                    await this.retry(this._context, this.ready);
                }
                else {
                    if (lastContext) {
                        debugLog?.log(`${this.name}: detach, context:`, lastContext);
                        await this.retry(lastContext, this.detach);
                        lastContext = null;
                    }
                    debugLog?.log(`${this.name}: attach, context:`, this._context);
                    await this.retry(this.context, this.attach);
                    lastContext = this._context;
                    this._whenFirstTimeReady.resolve(this._context);
                }

                debugLog?.log(`${this.name}: awaiting whenNotReady`);
                await this.source.whenNotReady(this._context, this._whenDisposeRequested);
                debugLog?.log(`${this.name}: unready, context:`, this._context);
                await this.retry(this._context, this.unready);
            }
            catch (e) {
                if (e instanceof OperationCancelledError)
                    break;
                errorLog?.log(`${this.name}.run: error:`, e);
            }
        }

        // Here we know that disposeAsync was called, so shutting down
        try {
            if (lastContext) {
                debugLog?.log(`${this.name}: detach, context:`, lastContext);
                await this.retry(this._context, this.detach);
                lastContext = null;
            }
        }
        catch (e) {
            if (!(e instanceof OperationCancelledError))
                errorLog?.log(`${this.name}.run: error:`, e);
        }
        debugLog?.log(`${this.name}: disposed`);
    }

    private async retry<T>(arg: T, fn?: (arg: T) => Promise<void> | void): Promise<void> {
        if (!fn)
            return ResolvedPromise.Void;

        return retryAsync(3, () => fn(arg));
    }
}

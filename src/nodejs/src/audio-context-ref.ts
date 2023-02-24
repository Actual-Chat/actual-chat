import { AsyncDisposable, ObjectDisposedError } from 'disposable';
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
    private readonly _whenDisposed = new PromiseSource<Cancelled>;
    private readonly _whenRunning: Promise<void>;
    private _context: AudioContext;
    private _whenReady = new PromiseSource<AudioContext>;
    private readonly name: string;

    public get context() { return this._context }

    constructor(
        public readonly source: AudioContextSource,
        public readonly onReady?: (context: AudioContext) => Promise<void> | void,
        public readonly onNotReady?: (context: AudioContext) => Promise<void> | void,
    ) {
        this.name = `AudioContextRef[${nextId++}]`
        this._whenRunning = this.run();
    }

    disposeAsync() : Promise<void> {
        if (!this._whenDisposed.isCompleted()) {
            debugLog?.log(`${this.name}.disposeAsync`)
            this._whenDisposed.resolve(cancelled)
        }
        return this._whenRunning;
    }

    public whenReady() {
        return waitAsync(this._whenReady, this._whenDisposed);
    }

    public whenDisposed() {
        return this._whenDisposed;
    }

    private async run(): Promise<void> {
        // This method should never throw!

        // We want to run the rest after completion of post-AudioContextSource.getRef logic
        await nextTickAsync();

        // noinspection InfiniteLoopJS
        while (!this._whenDisposed.isCompleted()) {
            try {
                this._context = await this.source.whenReady(this._whenDisposed);
                this._whenReady.resolve(this._context);
                debugLog?.log(`${this.name}: ready, context:`, this._context);
                await this.retry(this._context, this.onReady);

                await this.source.whenNotReady(this._context, this._whenDisposed);
                this._whenReady = new PromiseSource<AudioContext>();
                debugLog?.log(`${this.name}: not ready, context:`, this._context);
                await this.retry(this._context, this.onNotReady);
            }
            catch (e) {
                if (!(e instanceof OperationCancelledError))
                    errorLog?.log(`${this.name}.run: error:`, e);
            }
        }

        // Here we know that disposeAsync was called, so shutting down
        try {
            if (this._whenReady.isCompleted()) {
                this._whenReady = new PromiseSource<AudioContext>();
                await this.retry(this._context, this.onNotReady);
            }
        }
        catch (e) {
            if (!(e instanceof OperationCancelledError))
                errorLog?.log(`${this.name}.run: error:`, e);
        }
    }

    private async retry<T>(arg: T, fn?: (arg: T) => Promise<void> | void): Promise<void> {
        if (!fn)
            return ResolvedPromise.Void;

        return retryAsync(3, () => fn(arg));
    }
}

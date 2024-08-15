import { AsyncDisposable } from 'disposable';
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

export type AudioContextRefState = 'running' | 'paused';

export class AudioContextRef implements AsyncDisposable {
    private readonly name: string;
    private readonly _whenFirstTimeReady = new PromiseSource<OverridenAudioContext>;
    private readonly whenRunning: Promise<void>;
    private readonly whenDisposeRequested = new PromiseSource<Cancelled>;
    private context: OverridenAudioContext;
    private _state: AudioContextRefState = 'paused';

    public get state(): AudioContextRefState { return this._state; }

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
        this._state = 'paused';
        if (!this.whenDisposeRequested.isCompleted())
            this.whenDisposeRequested.resolve(cancelled)

        this._state = 'paused';
        this.source.pauseRef();
        await this.whenRunning;
    }

    public whenFirstTimeReady() {
        return waitAsync(this._whenFirstTimeReady, this.whenDisposeRequested);
    }

    public async whenDisposed() {
        await this.whenRunning;
    }

    public use(): () => void {
        this._state = 'running';
        this.source.useRef();
        return () => {
            this._state = 'paused';
            this.source.pauseRef();
        };
    }

    private async maintain(): Promise<void> {
        // This method should never throw!

        // We want to do the rest after completion of post-AudioContextSource.getRef logic
        await nextTickAsync();

        let lastContext: AudioContext = null;
        try {
            while (!this.whenDisposeRequested.isCompleted()) {
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
                    this._whenFirstTimeReady.resolve(this.context);
                }

                debugLog?.log(`${this.name}: awaiting whenNotReady`);
                await this.source.whenNotReady(this.context, this.whenDisposeRequested);
                if (!this.source.isActive || this.context.state === 'closed')
                    await this.options.detach?.(this.context);
            }
        }
        catch (e) {
            const mustReport = !((e instanceof OperationCancelledError) && this.whenDisposeRequested.isCompleted());
            if (mustReport)
                errorLog?.log(`${this.name}.maintain: error:`, e);
        }

        debugLog?.log(`${this.name}.maintain: shutting down...`);
        if (!this.whenDisposeRequested.isCompleted())
            this.whenDisposeRequested.resolve(cancelled); // Just for the consistency

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

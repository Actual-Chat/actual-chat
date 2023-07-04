import Denque from 'denque';
import { PromiseSource } from 'promises';
import { Log } from 'logging';

const { debugLog, errorLog } = Log.get('AsyncProcessor');

export class AsyncProcessor<T> {
    private readonly queue = new Denque<T>();
    private whenReadyToResume = new PromiseSource<void>();
    private _isRunning = true;

    public get isRunning(): boolean { return this._isRunning }
    public readonly whenRunning: Promise<void>;

    constructor(
        private readonly name: string,
        private readonly process: (item: T) => Promise<boolean>,
    ) {
        this.whenRunning = this.run();
    }

    public enqueue(item: T, mustFailIfAlreadyStopped = true) {
        if (!this._isRunning) {
            if (mustFailIfAlreadyStopped)
                throw new Error(`${this.name} is already stopping or stopped.`);
            return;
        }

        this.queue.push(item);
        if (!this.whenReadyToResume.isCompleted())
            this.whenReadyToResume.resolve(undefined);
    }

    public clearQueue() {
        this.queue.clear();
    }

    private async run(): Promise<void> {
        debugLog?.log(`${this.name} started.`);
        try {
            for (;;) {
                while (this.queue.length) {
                    const item = this.queue.pop();
                    this._isRunning &&= await this.process(item);
                    if (!this._isRunning) {
                        debugLog?.log(`${this.name} stopped.`);
                        return;
                    }
                }
                await this.whenReadyToResume;
                this.whenReadyToResume = new PromiseSource<void>();
            }
        }
        catch (e) {
            errorLog?.log(`${this.name} failed:`, e);
            throw e;
        }
        finally {
            this.queue.clear();
        }
    }
}

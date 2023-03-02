import Denque from 'denque';
import { PromiseSource } from 'promises';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AsyncProcessor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
// eslint-disable-next-line @typescript-eslint/no-unused-vars
const errorLog = Log.get(LogScope, LogLevel.Error);

export class AsyncProcessor<T> {
    private readonly queue = new Denque<T>();
    private whenReadyToResume = new PromiseSource<void>();
    private isRunning: boolean;
    private mustStop: boolean;

    public readonly whenRunning: Promise<void>;

    constructor(
        private readonly name: string,
        private readonly process: (item: T) => Promise<boolean>,
    ) {
        this.whenRunning = this.run();
    }

    public enqueue(item: T, mustFailIfAlreadyStopped = true) {
        if (!this.isRunning || this.mustStop) {
            if (mustFailIfAlreadyStopped)
                throw new Error(`${this.name} is already stopping or stopped.`);
            return;
        }

        this.queue.push(item);
        this.resume();
    }

    public clearQueue() {
        this.queue.clear();
    }

    public stop(): Promise<void> {
        this.mustStop = true;
        this.resume();
        return this.whenRunning;
    }

    private async run(): Promise<void> {
        this.isRunning = true;
        debugLog?.log(`${this.name} started.`);
        try {
            while (!this.mustStop) {
                while (this.queue.length) {
                    if (this.mustStop)
                        return;
                    const item = this.queue.pop();
                    const shouldContinue = await this.process(item);
                    if (!shouldContinue) {
                        this.mustStop = true;
                        return;
                    }
                }
                await this.whenReadyToResume;
                this.whenReadyToResume = new PromiseSource<void>();
            }
            debugLog?.log(`${this.name} stopped.`);
        }
        catch (e) {
            errorLog?.log(`${this.name} failed:`, e);
            throw e;
        }
        finally {
            this.queue.clear();
            this.isRunning = false;
        }
    }

    private resume(): void {
        if (!this.whenReadyToResume.isCompleted())
            this.whenReadyToResume.resolve(undefined);
    }
}

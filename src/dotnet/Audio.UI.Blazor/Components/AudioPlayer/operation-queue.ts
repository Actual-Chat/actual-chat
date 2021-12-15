import Denque from 'denque';

export interface Operation {
    execute: () => Promise<void>;
    onStart: () => void;
    onSuccess: () => void;
    onError: (error: Error) => void;
}

export class OperationQueue {
    private readonly _debugMode: boolean;
    private readonly _queue: Denque<Operation>;

    constructor(debugMode: boolean) {
        this._debugMode = debugMode;
        this._queue = new Denque<Operation>();
    }

    public abort() {
        if (this._debugMode)
            this.log("Enqueue 'Abort' operation.");
        this._queue.unshift({
            execute: () => { this._queue.clear(); return Promise.resolve(); },
            onSuccess: () => {
                if (this._debugMode)
                    this.log("Aborted.");
            },
            onStart: () => { },
            onError: error => {
                if (this._debugMode)
                    this.logWarn(`Can't abort operations in queue. Error: ${error.message}, ${error.stack}`);
            }
        });
    }

    public append(operation: Operation): void {
        this._queue.push(operation);
    }

    public prepend(operation: Operation): void {
        this._queue.unshift(operation);
    }

    public async executeNext(): Promise<boolean> {
        const queue = this._queue;
        if (queue.length > 0) {
            const operation: Operation = queue.shift();
            try {
                operation.onStart();
                await operation.execute();
                operation.onSuccess();
            }
            catch (error) {
                if (this._debugMode) {
                    this.logError("Unhandled exception executing the current operation. Error: " +
                        (error instanceof Error ? `${error.message}, ${error.stack}` : error));
                }
                operation.onError(error as Error);
            }
            finally {
                return this._queue.length > 0;
            }
        }
    }

    private log(message: string) {
        console.debug(`[${new Date(Date.now()).toISOString()}] OperationQueue: ${message}`);
    }
    private logWarn(message: string) {
        console.warn(`[${new Date(Date.now()).toISOString()}] OperationQueue: ${message}`);
    }
    private logError(message: string) {
        console.error(`[${new Date(Date.now()).toISOString()}] OperationQueue: ${message}`);
    }
}

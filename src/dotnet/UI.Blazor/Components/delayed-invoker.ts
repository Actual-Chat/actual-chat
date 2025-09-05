import { Log } from 'logging';

const { debugLog, errorLog } = Log.get('DelayedInvoker');

export class DelayedInvoker {
    private callbacks: Array<() => void | Promise<void>> = [];
    private currentResolve: (() => void) | null = null;
    private currentPromise: Promise<void> = this.createNewPromise();

    private createNewPromise(): Promise<void> {
        return new Promise<void>((resolve) => {
            this.currentResolve = resolve;
        });
    }

    public registerCallback(cb: () => void | Promise<void>): Promise<void> {
        this.callbacks.push(cb);
        return this.currentPromise;
    }

    public async invoke(): Promise<void> {
        try {
            debugLog?.log("-> invoke, callbacks count: " + this.callbacks.length);
            await Promise.all(this.callbacks.map(async (cb) => {
                try {
                    await cb();
                } catch (e) {
                    errorLog?.log("An error occured during callback processing:", e);
                }
            }));
            debugLog?.log("<- invoke");
        } finally {
            this.callbacks = [];

            if (this.currentResolve) {
                this.currentResolve();
                this.currentResolve = null;
            }

            this.currentPromise = this.createNewPromise();
        }
    }
}

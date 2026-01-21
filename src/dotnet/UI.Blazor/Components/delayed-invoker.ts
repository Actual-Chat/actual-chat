import { Log } from 'logging';
import { EventHandlerSet } from 'event-handling';

const { debugLog, errorLog } = Log.get('DelayedInvoker');

export class DelayedInvoker {
    private callbacks: Array<() => void | Promise<void>> = [];
    private currentResolve: (() => void) | null = null;
    private currentPromise: Promise<void> = this.createNewPromise();

    public readonly hasCallbacksChanged = new EventHandlerSet<void>();

    private createNewPromise(): Promise<void> {
        return new Promise<void>((resolve) => {
            this.currentResolve = resolve;
        });
    }

    public registerCallback(cb: () => void | Promise<void>): Promise<void> {
        const hasCallbacks = this.hasCallbacks();
        this.callbacks.push(cb);
        if (!hasCallbacks) {
            setTimeout(() => {
                this.hasCallbacksChanged.triggerSilently();
            }, 0);
        }
        return this.currentPromise;
    }

    public unregisterCallback(cb: () => void | Promise<void>) : boolean {
        const hasCallbacks = this.hasCallbacks();
        const index = this.callbacks.indexOf(cb);
        if (index < 0)
            return false;
        this.callbacks.splice(index, 1);
        const hasCallbacks2 = this.hasCallbacks();
        if (hasCallbacks != hasCallbacks2) {
            setTimeout(() => {
                this.hasCallbacksChanged.triggerSilently();
            }, 0);
        }
        return true;
    }

    public hasCallbacks(): boolean {
        return this.callbacks.length > 0;
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

            this.hasCallbacksChanged.triggerSilently();
        }
    }
}

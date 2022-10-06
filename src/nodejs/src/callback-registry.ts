export class CallbackRegistry<TResult> {
    private lastCallbackId = 0;
    public callbacks = new Map<number, () => TResult>();

    public nextCallbackId() {
        return ++this.lastCallbackId;
    }

    public register(callback: () => TResult, callbackId?: number) : number {
        if (callbackId == null)
            callbackId = this.nextCallbackId();
        else if (this.callbacks.has(callbackId))
            throw new Error(`Callback #${callbackId} is already registered.`);

        this.callbacks.set(callbackId, callback);
        return callbackId;
    }

    public extract(callbackId: number): () => TResult {
        const callback = this.callbacks.get(callbackId);
        if (callback === undefined)
            throw new Error(`Callback #${callbackId} is not found.`);

        this.callbacks.delete(callbackId);
        return callback;
    }

    public invoke(callbackId: number): TResult {
        const callback = this.extract(callbackId);
        return callback();
    }
}

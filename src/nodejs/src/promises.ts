import { Handler } from 'first-interaction';

export class PromiseSource<T> implements Promise<T> {
    public resolve: (T) => void;
    public reject: (any) => void;

    private readonly _promise: Promise<T>;
    private _timeoutHandle?: number = null;
    private _isResolved = false;

    constructor() {
        let resolve: (T) => void = null;
        let reject: (any) => void = null;
        this._promise = new Promise<T>((resolve1, reject1) => {
            resolve = (value: T) => {
                if (this._isResolved)
                    return;
                this._isResolved = true;
                this.clearTimeout();
                resolve1(value);
            };
            reject = (reason: unknown) => {
                if (this._isResolved)
                    return;
                this._isResolved = true;
                this.clearTimeout();
                reject1(reason);
            };
        })
        this.resolve = resolve;
        this.reject = reject;
        this[Symbol.toStringTag] = this._promise[Symbol.toStringTag];
    }

    public isResolved() : boolean {
        return this._isResolved;
    }

    public clearTimeout() : void {
        if (this._timeoutHandle != null)
            window.clearTimeout(this._timeoutHandle);
    }

    public setTimeout(timeout: number, handler?: (promise: PromiseSource<T>) => void) : void {
        if (this._timeoutHandle != null)
            window.clearTimeout(this._timeoutHandle);
        if (timeout == null)
            return;

        this._timeoutHandle = window.setTimeout(() => {
            if (handler != null)
                handler(this);
            else {
                const error = new Error('The promise has timed out.');
                this.reject(error);
            }
        }, timeout)
    }

    // PromiseLike<T> implementation

    readonly [Symbol.toStringTag]: string;

    then<TResult1 = T, TResult2 = never>(
        onfulfilled?: ((value: T) => (PromiseLike<TResult1> | TResult1)) | undefined | null,
        onrejected?: ((reason: any) => (PromiseLike<TResult2> | TResult2)) | undefined | null): Promise<TResult1 | TResult2> {
        return this._promise.then(onfulfilled, onrejected);
    }

    catch<TResult = never>(onrejected?: ((reason: any) => (PromiseLike<TResult> | TResult)) | undefined | null): Promise<T | TResult> {
        return this._promise.catch(onrejected);
    }

    finally(onfinally?: (() => void) | undefined | null): Promise<T> {
        return this._promise.finally(onfinally);
    }
}

/** Async version of setTimeout */
export function delayAsync(timeout: number): PromiseSource<void> {
    const promise = new PromiseSource<void>();
    promise.setTimeout(timeout, p => p.resolve(null))
    return promise;
}

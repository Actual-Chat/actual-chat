const LogScope = 'promises';

export function isPromise<T, S>(obj: PromiseLike<T> | S): obj is PromiseLike<T> {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['then'] === 'function';
}

export class PromiseSource<T> implements Promise<T> {
    public resolve: (T) => void;
    public reject: (any) => void;

    private readonly _promise: Promise<T>;
    private _timeoutHandle: ReturnType<typeof setTimeout> | null = null;
    private _isCompleted = false;

    constructor() {
        this._promise = new Promise<T>((resolve1, reject1) => {
            this.resolve = (value: T) => {
                if (this._isCompleted)
                    return;
                this._isCompleted = true;
                this.clearTimeout();
                resolve1(value);
            };
            this.reject = (reason: unknown) => {
                if (this._isCompleted)
                    return;
                this._isCompleted = true;
                this.clearTimeout();
                reject1(reason);
            };
        })
        this[Symbol.toStringTag] = this._promise[Symbol.toStringTag];
    }

    public isResolved() : boolean {
        return this._isCompleted;
    }

    public isCompleted() : boolean {
        return this._isCompleted;
    }

    public clearTimeout() : void {
        if (this._timeoutHandle == null)
            return;

        clearTimeout(this._timeoutHandle);
        this._timeoutHandle = null;
    }

    public setTimeout(timeout: number, handler?: () => void) : void {
        this.clearTimeout();
        if (timeout == null)
            return;

        this._timeoutHandle = setTimeout(() => {
            this._timeoutHandle = null;
            if (handler != null)
                handler();
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
        onrejected?: ((reason: any) => (PromiseLike<TResult2> | TResult2)) | undefined | null
    ): Promise<TResult1 | TResult2> {
        return this._promise.then(onfulfilled, onrejected);
    }

    catch<TResult = never>(
        onrejected?: ((reason: any) => (PromiseLike<TResult> | TResult)) | undefined | null
    ): Promise<T | TResult> {
        return this._promise.catch(onrejected);
    }

    finally(onfinally?: (() => void) | undefined | null): Promise<T> {
        return this._promise.finally(onfinally);
    }
}

/** Async version of setTimeout */
export function delayAsync(timeout: number): PromiseSource<void> {
    const promise = new PromiseSource<void>();
    promise.setTimeout(timeout, () => promise.resolve(undefined))
    return promise;
}

export function flexibleDelayAsync(getNextTimeout: () => number): PromiseSource<void> {
    // eslint-disable-next-line no-constant-condition
    const promise = new PromiseSource<void>();
    const timeoutHandler = () => {
        const timeout = getNextTimeout();
        if (timeout <= 0)
            promise.resolve(undefined);
        else
            promise.setTimeout(timeout, timeoutHandler);
    };
    promise.setTimeout(getNextTimeout(), timeoutHandler);
    return promise;
}

// nextTick & nextTickAsync:
// They're quite similar to polyfill of
// [setImmediate](https://developer.mozilla.org/en-US/docs/Web/API/Window/setImmediate),
// which we don't use because it relies on setTimeout, which is throttled in background tabs.
export function nextTick(callback: () => unknown) {
    nextTickCallbacks.push(callback);
    nextTickChannel.port2.postMessage(null);
}

export function nextTickAsync(): Promise<void> {
    return new Promise<void>(resolve => nextTick(resolve));
}

const nextTickCallbacks: Array<() => unknown> = [];
const nextTickChannel = new MessageChannel();
// eslint-disable-next-line @typescript-eslint/no-unused-vars
nextTickChannel.port1.onmessage = _ => {
    const callback = nextTickCallbacks.shift();
    callback();
};

// Throttle & debounce

export interface ResettableFunc<T extends (...args: unknown[]) => unknown> {
    (...args: Parameters<T>): void;
    reset(): void;
}

class Call<T extends (...args: unknown[]) => unknown> {
    constructor(
        readonly func: (...args: Parameters<T>) => ReturnType<T>,
        readonly self: unknown,
        readonly parameters: Parameters<T>
    ) { }

    public invoke() : unknown {
        return this.func.apply(this.self, this.parameters);
    }

    public invokeSafely() : unknown {
        try {
            return this.invoke();
        }
        catch (error) {
            console.error(`${LogScope}.Call.invokeSafely failed with an error:`, error)
        }
    }
}

export function throttle<T extends (...args: unknown[]) => unknown>(
    func: (...args: Parameters<T>) => ReturnType<T>,
    interval: number
) : ResettableFunc<T> {
    let lastCall: Call<T> | null = null;
    let lastFireTime = 0;
    let timeoutHandle: ReturnType<typeof setTimeout> | null = null;

    const reset = (mustClearTimeout: boolean, newLastFireTime: number) => {
        if (mustClearTimeout && timeoutHandle !== null)
            clearTimeout(timeoutHandle);
        timeoutHandle = lastCall = null;
        lastFireTime = newLastFireTime;
    }

    const getFireDelay = () => Math.max(0, lastFireTime + interval - Date.now());

    const fire = () => {
        const call = lastCall;
        reset(false, Date.now());
        call?.invoke(); // We need to do this at the very end
    };

    const result: ResettableFunc<T> = function(...callArgs: Parameters<T>): void {
        lastCall = new Call<T>(func, this, callArgs);
        if (timeoutHandle !== null)
            return;

        const fireDelay = getFireDelay();
        if (fireDelay > 0) {
            timeoutHandle = setTimeout(fire, fireDelay);
            return;
        }

        fire(); // We need to do this at the very end
    }
    result.reset = () => reset(true, 0);
    return result;
}

export function debounce<T extends (...args: unknown[]) => unknown>(
    func: (...args: Parameters<T>) => ReturnType<T>,
    interval: number,
    debounceHead = false,
) : ResettableFunc<T> {
    let lastCall: Call<T> | null = null;
    let lastCallTime = 0;
    let timeoutPromise: PromiseSource<void> | null = null;

    const reset = (newLastCallTime: number) => {
        timeoutPromise?.reject(undefined);
        timeoutPromise = lastCall = null;
        lastCallTime = newLastCallTime;
    }

    const getFireDelay = () => Math.max(0, lastCallTime + interval - Date.now());

    const fire = () => {
        const call = lastCall;
        reset(lastCallTime);
        call?.invoke(); // We need to do this at the very end
    };

    const result: ResettableFunc<T> = function(...callArgs: Parameters<T>): void {
        const isHead = getFireDelay() == 0;
        lastCall = new Call<T>(func, this, callArgs);
        lastCallTime = Date.now();
        if (timeoutPromise !== null)
            return;

        if (debounceHead || !isHead) {
            timeoutPromise = flexibleDelayAsync(getFireDelay);
            void timeoutPromise.then(fire);
            return;
        }

        fire(); // We need to do this at the very end
    };
    result.reset = () => reset(0);
    return result;
}

export function serialize<T extends (...args: unknown[]) => PromiseLike<TResult>, TResult>(
    func: (...args: Parameters<T>) => PromiseLike<TResult>,
    limit: number | null = null
) : (...sArgs: Parameters<T>) => Promise<TResult> {
    let lastCall: Promise<TResult> = Promise.resolve(null as TResult);
    let queueSize = 0;

    return function(...callArgs: Parameters<T>): Promise<TResult> {
        if (limit != null && queueSize >= limit)
            return lastCall;
        queueSize++;
        const prevCall = lastCall;
        return lastCall = (async () => {
            try {
                await prevCall;
                return (await func.apply(this, callArgs)) as TResult;
            }
            finally {
                queueSize--;
            }
        })();
    }
}

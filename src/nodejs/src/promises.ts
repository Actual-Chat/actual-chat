import { Log, LogLevel } from 'logging';
import { PreciseTimeout, Timeout } from 'timeout';

const LogScope = 'promises';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const errorLog = Log.get(LogScope, LogLevel.Error);

export function isPromise<T, S>(obj: PromiseLike<T> | S): obj is PromiseLike<T> {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['then'] === 'function';
}

export class AsyncLock {
    private _promise: Promise<Unit>;

    constructor () {
        this._promise = Promise.resolve(Unit.Instance);
    }

    public async lock<T>(job: () => Promise<T>): Promise<T> {
        await this._promise;
        const task = new PromiseSource<Unit>();
        this._promise = task;
        try {
            return await job();
        }
        finally {
            task.resolve(Unit.Instance);
        }
    }
}

class Unit {
    static readonly Instance = new Unit();
}

export class PromiseSource<T> implements Promise<T> {
    public resolve: (T) => void;
    public reject: (any) => void;

    private readonly _promise: Promise<T>;
    private _timeout: Timeout = null;
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

    public isResolved(): boolean {
        return this._isCompleted;
    }

    public isCompleted(): boolean {
        return this._isCompleted;
    }

    public setTimeout(timeoutMs: number | null, callback?: () => unknown): void {
        this.clearTimeout();
        if (timeoutMs == null)
            return;

        this._timeout = new Timeout(timeoutMs, () => {
            this._timeout = null;
            if (callback != null)
                callback();
            else {
                const error = new Error('The promise has timed out.');
                this.reject(error);
            }
        })
    }

    public setPreciseTimeout(timeoutMs: number | null, callback?: () => unknown): void {
        this.clearTimeout();
        if (timeoutMs == null)
            return;

        this._timeout = new PreciseTimeout(timeoutMs, () => {
            this._timeout = null;
            if (callback != null)
                callback();
            else {
                const error = new Error('The promise has timed out.');
                this.reject(error);
            }
        })
    }

    public clearTimeout(): void {
        if (this._timeout == null)
            return;

        this._timeout.clear();
        this._timeout = null;
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

// Precise timeout (~ 8-16ms or so?) based on requestAnimationFrame

// Async versions of setTimeout

export function delayAsync(delayMs: number): PromiseSource<void> {
    const promise = new PromiseSource<void>();
    promise.setTimeout(delayMs, () => promise.resolve(undefined))
    return promise;
}

export function preciseDelayAsync(delayMs: number): PromiseSource<void> {
    const promise = new PromiseSource<void>();
    promise.setPreciseTimeout(delayMs, () => promise.resolve(undefined))
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

    public invoke(): unknown {
        return this.func.apply(this.self, this.parameters);
    }

    public invokeSafely(): unknown {
        try {
            return this.invoke();
        }
        catch (error) {
            errorLog?.log(`Call.invokeSafely: unhandled error:`, error)
        }
    }
}

export type ThrottleMode = 'default' | 'skip' | 'delayHead';

export function throttle<T extends (...args: unknown[]) => unknown>(
    func: (...args: Parameters<T>) => ReturnType<T>,
    interval: number,
    mode: ThrottleMode = 'default'
): ResettableFunc<T> {
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

        if (mode === 'delayHead') {
            lastFireTime = Date.now();
            timeoutHandle = setTimeout(fire, getFireDelay());
            return;
        }

        const fireDelay = getFireDelay();
        if (fireDelay > 0) {
            if (mode !== 'skip')
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
): ResettableFunc<T> {
    let lastCall: Call<T> | null = null;
    let lastCallTime = 0;
    let timeoutPromise: PromiseSource<void> | null = null;

    const reset = (newLastCallTime: number) => {
        timeoutPromise?.clearTimeout();
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
): (...sArgs: Parameters<T>) => Promise<TResult> {
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

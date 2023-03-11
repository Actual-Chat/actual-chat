import { Log } from 'logging';
import { PreciseTimeout, Timeout } from 'timeout';
import { Disposable } from 'disposable';

const { logScope, debugLog, warnLog, errorLog } = Log.get('promises');

export class TimedOut {
    public static readonly instance: TimedOut = new TimedOut();
}

export function isPromise<T, S>(obj: PromiseLike<T> | S): obj is PromiseLike<T> {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['then'] === 'function';
}

export class PromiseSource<T> implements Promise<T> {
    public resolve: (T) => void;
    public reject: (any) => void;

    private readonly _promise: Promise<T>;
    private _isCompleted = false;

    constructor(resolve?: ((value: T) => void), reject?: ((reason?: unknown) => void)) {
        this._promise = new Promise<T>((resolve1, reject1) => {
            this.resolve = (value: T) => {
                if (this._isCompleted)
                    return;

                this._isCompleted = true;
                resolve1(value);
                if (resolve)
                    resolve(value);
            };
            this.reject = (reason: unknown) => {
                if (this._isCompleted)
                    return;

                this._isCompleted = true;
                reject1(reason);
                if (reject)
                    reject(reason);
            };
        })
        this[Symbol.toStringTag] = this._promise[Symbol.toStringTag];
    }

    public isCompleted(): boolean {
        return this._isCompleted;
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

export class PromiseSourceWithTimeout<T> extends PromiseSource<T> {
    private _timeout: Timeout = null;

    constructor(resolve?: ((value: T) => void), reject?: ((reason?: unknown) => void)) {
        super((value: T) => {
            this.clearTimeout();
            if (resolve)
                resolve(value);
        }, (reason: unknown) => {
            this.clearTimeout();
            if (reject)
                reject(reason);
        });
    }

    public hasTimeout(): boolean {
        return this._timeout != null;
    }

    public setTimeout(timeoutMs: number | null, callback?: () => unknown): void {
        if (this._timeout) {
            this._timeout.clear();
            this._timeout = null;
        }
        if (timeoutMs == null || this.isCompleted())
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
        if (this._timeout) {
            this._timeout.clear();
            this._timeout = null;
        }
        if (timeoutMs == null || this.isCompleted())
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
}

// Cancellation

export type Cancelled = symbol;
export const cancelled : Cancelled = Symbol('Cancelled');
export class OperationCancelledError extends Error {
    constructor(message?: string) {
        super(message ?? 'The operation is cancelled.');
    }
}

export async function waitAsync<T>(promise: PromiseLike<T>, cancel?: Promise<Cancelled>): Promise<T> {
    if (cancel === undefined)
        return await promise;

    const result = await Promise.race([promise, cancel]);
    if (result === cancelled)
        throw new OperationCancelledError();
    return await promise;
}

// Async versions of setTimeout

export function delayAsync(delayMs: number): PromiseSourceWithTimeout<void> {
    const promise = new PromiseSourceWithTimeout<void>();
    promise.setTimeout(delayMs, () => promise.resolve(undefined))
    return promise;
}

export function preciseDelayAsync(delayMs: number): PromiseSourceWithTimeout<void> {
    const promise = new PromiseSourceWithTimeout<void>();
    promise.setPreciseTimeout(delayMs, () => promise.resolve(undefined))
    return promise;
}

export function flexibleDelayAsync(getNextTimeout: () => number): PromiseSourceWithTimeout<void> {
    // eslint-disable-next-line no-constant-condition
    const promise = new PromiseSourceWithTimeout<void>();
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

    public invokeSilently(): unknown {
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
    intervalMs: number,
    mode: ThrottleMode = 'default',
    name : string | undefined = undefined
): ResettableFunc<T> {
    let lastCall: Call<T> | null = null;
    let nextFireTime = 0;
    let timeoutHandle: ReturnType<typeof setTimeout> | null = null;

    const reset = () => {
        if (timeoutHandle !== null)
            clearTimeout(timeoutHandle);
        timeoutHandle = lastCall = null;
        nextFireTime = 0;
    }

    const fire = () => {
        if (timeoutHandle !== null)
            clearTimeout(timeoutHandle);
        timeoutHandle = null;
        nextFireTime = 0;
        if (lastCall !== null) {
            if (name)
                debugLog?.log(`throttle '${name}': fire`);
            const call = lastCall;
            lastCall = null;
            call?.invokeSilently(); // This must be done at last
        }
        else {
            if (name)
                debugLog?.log(`throttle '${name}': delay ended`);
        }
    };

    const result: ResettableFunc<T> = function(...callArgs: Parameters<T>): void {
        const call = new Call<T>(func, this, callArgs);
        const fireDelay = nextFireTime - Date.now();
        if (timeoutHandle !== null && fireDelay <= 0) {
            // Our delayed "fire" is ready to fire but not fired yet,
            // so we "flush" it here.
            fire();
        }

        if (timeoutHandle === null) {
            // lastCall is null here
            if (mode === 'delayHead') {
                if (name)
                    debugLog?.log(`throttle '${name}': delaying head call`);
                lastCall = call;
            } else {
                if (name)
                    debugLog?.log(`throttle '${name}': fire (head call)`);
                call?.invokeSilently();
            }
            nextFireTime = Date.now() + intervalMs;
            timeoutHandle = setTimeout(fire, intervalMs);
        } else {
            // timeoutHandle !== null, so all we need to do here is to update lastCall
            if (name)
                debugLog?.log(`throttle '${name}': throttling, remaining delay = ${fireDelay}ms`);
            if (mode !== 'skip') // i.e. default or delayHead
                lastCall = call;
        }
    }
    result.reset = reset;
    return result;
}

export function debounce<T extends (...args: unknown[]) => unknown>(
    func: (...args: Parameters<T>) => ReturnType<T>,
    intervalMs: number,
    debounceHead = false,
    name : string | undefined = undefined
): ResettableFunc<T> {
    let lastCall: Call<T> | null = null;
    let nextFireTime = 0;
    let timeoutHandle: ReturnType<typeof setTimeout> | null = null;

    const reset = () => {
        if (timeoutHandle !== null)
            clearTimeout(timeoutHandle);
        timeoutHandle = lastCall = null;
        nextFireTime = 0;
    }

    const fire = () => {
        if (timeoutHandle !== null)
            clearTimeout(timeoutHandle);
        timeoutHandle = null;
        nextFireTime = 0;
        if (lastCall !== null) {
            if (name)
                debugLog?.log(`debounce '${name}': fire`);
            const call = lastCall;
            lastCall = null;
            call?.invokeSilently(); // This must be done at last
        }
        else {
            if (name)
                debugLog?.log(`debounce '${name}': delay ended`);
        }
    };

    const result: ResettableFunc<T> = function(...callArgs: Parameters<T>): void {
        const call = new Call<T>(func, this, callArgs);
        const fireDelay = nextFireTime - Date.now();
        if (timeoutHandle !== null && fireDelay <= 0) {
            // Our delayed "fire" is ready to fire but not fired yet,
            // so we "flush" it here.
            fire();
        }

        if (timeoutHandle === null) {
            // lastCall is null here
            if (debounceHead) {
                if (name)
                    debugLog?.log(`debounce '${name}': debouncing head call`);
                lastCall = call;
            } else {
                if (name)
                    debugLog?.log(`debounce '${name}': fire (head call)`);
                call?.invokeSilently();
            }
            nextFireTime = Date.now() + intervalMs;
            timeoutHandle = setTimeout(fire, intervalMs);
        } else {
            // timeoutHandle !== null, so all we need to do here is to update lastCall
            if (name)
                debugLog?.log(`debounce '${name}': debouncing`);
            lastCall = call;
            clearTimeout(timeoutHandle);
            timeoutHandle = setTimeout(fire, intervalMs);
        }
    };
    result.reset = reset;
    return result;
}

// Serialize

export function serialize<T extends (...args: unknown[]) => PromiseLike<TResult> | TResult, TResult>(
    func: (...args: Parameters<T>) => PromiseLike<TResult> | TResult,
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

// Retry & catchErrors

type RetryDelaySeq = (tryIndex: number) => number;
const defaultRetryDelays: RetryDelaySeq = () => 50;

export async function retryForever<TResult>(
    fn: (tryIndex: number, lastError: unknown) => PromiseLike<TResult> | TResult,
    retryDelays?: RetryDelaySeq,
) : Promise<TResult> {
    retryDelays ??= defaultRetryDelays;
    let lastError: unknown = undefined;
    for (let tryIndex = 0;;) {
        try {
            return await fn(tryIndex, lastError);
        }
        catch (e) {
            lastError = e;
        }
        ++tryIndex;
        warnLog?.log(`retry(${tryIndex}): error:`, lastError);
        await delayAsync(retryDelays(tryIndex));
    }
}

export async function retry<TResult>(
    tryCount: number,
    fn: (tryIndex: number, lastError: unknown) => PromiseLike<TResult> | TResult,
    retryDelays?: RetryDelaySeq,
) : Promise<TResult> {
    retryDelays ??= defaultRetryDelays;
    let lastError: unknown = undefined;
    for (let tryIndex = 0;;) {
        if (tryIndex >= tryCount)
            throw lastError;

        try {
            return await fn(tryIndex, lastError);
        }
        catch (e) {
            lastError = e;
        }
        ++tryIndex;
        warnLog?.log(`retry(${tryIndex}/${tryCount}): error:`, lastError);
        await delayAsync(retryDelays(tryIndex));
    }
}

export async function catchErrors<TResult>(
    fn: () => PromiseLike<TResult> | TResult,
    onError?: (e: unknown) => TResult,
) : Promise<TResult> {
    try {
        return await fn();
    }
    catch (e) {
        return onError ? onError(e) : undefined;
    }
}

export class AsyncLockReleaser implements Disposable {
    private readonly _whenReleased: PromiseSource<void>;
    constructor(public readonly asyncLock: AsyncLock) {
        if (asyncLock.releaser != null)
            throw new Error(`${logScope}.AsyncLockReleaser cannot be created while the lock is held.`);

        asyncLock.releaser = this;
        this._whenReleased = new PromiseSource<void>(
            () => {
                if (asyncLock.releaser != this)
                    throw new Error(`${logScope}.AsyncLockReleaser is associated with another releaser.`);

                asyncLock.releaser = null;
                return;
            },
            () => `${logScope}.AsyncLockReleaser.released cannot be rejected.`);
    }

    public whenReleased(): Promise<void> {
        return this._whenReleased;
    }

    dispose(): void {
        this._whenReleased.resolve(undefined);
    }
}

export class AsyncLock {
    public releaser: AsyncLockReleaser = null;

    public async lock(): Promise<AsyncLockReleaser> {
        if (this.releaser != null)
            await this.releaser.whenReleased();
        return new AsyncLockReleaser(this);
    }
}

export class ResolvedPromise {
    public static readonly Void = new PromiseSource<void>();
    public static readonly True = new PromiseSource<boolean>();
    public static readonly False = new PromiseSource<boolean>();
}
ResolvedPromise.Void.resolve(undefined);
ResolvedPromise.True.resolve(true);
ResolvedPromise.False.resolve(false);

// Self-test - we don't want to run it in workers & worklets
const mustRunSelfTest = debugLog != null && globalThis['focus'];
if (mustRunSelfTest) {
    const testLog = errorLog;
    if (!testLog)
        throw new Error('testLog == null');
    void (async () => {
        const c = new PromiseSource<Cancelled>();
        const p = waitAsync(delayAsync(1000), c);
        c.resolve(cancelled);
        try {
            await p;
            throw new Error('Failed!');
        }
        catch (e) {
            if (!(e instanceof OperationCancelledError))
                throw e;
        }
    })();
}

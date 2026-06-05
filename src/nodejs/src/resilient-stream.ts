import { getLogs } from 'logging';
import { PromiseSource, delayAsync, abortPromise } from 'actuallab-core';

const { warnLog } = getLogs('ResilientStream');

// --- Retry delay sequences ---

export type RetryDelayFn = (tryIndex: number) => number;

export function expRetryDelays(minMs: number, maxMs: number): RetryDelayFn {
    return (tryIndex: number) => Math.min(minMs * Math.pow(2, tryIndex), maxMs);
}

// --- Options ---

export interface ResilientStreamOptions<T> {
    provider: (signal?: AbortSignal) => AsyncIterable<T>;
    resetItem?: T;
    retryDelays?: RetryDelayFn;
    maxRetries?: number; // undefined = infinite
    isInfinite?: boolean; // when set, a normal completion is treated as a transient drop and reconnected
    signal?: AbortSignal;
}

// --- ResilientStream ---

/**
 * A reliability wrapper that retries the source async iterable on transient failures,
 * presenting a seamless AsyncIterable to the consumer.
 */
export function resilientStream<T>(options: ResilientStreamOptions<T>): AsyncIterable<T> {
    const {
        provider,
        resetItem,
        retryDelays = expRetryDelays(100, 5000),
        maxRetries,
        isInfinite,
        signal,
    } = options;

    return {
        [Symbol.asyncIterator](): AsyncIterator<T> {
            const buffer: T[] = [];
            let done = false;
            let error: unknown = undefined;
            let waitingConsumer: PromiseSource<void> | null = null;

            const isAborted = (): boolean => signal?.aborted === true;

            // Start the background producer
            void pushItems();

            async function pushItems(): Promise<void> {
                let failedTryCount = 0;
                try {
                    for (;;) {
                        try {
                            const source = provider(signal);
                            for await (const item of source) {
                                if (isAborted()) return;
                                failedTryCount = 0;
                                buffer.push(item);
                                waitingConsumer?.resolve(undefined);
                                waitingConsumer = null;
                            }
                            if (!isInfinite) {
                                // Normal completion
                                done = true;
                                waitingConsumer?.resolve(undefined);
                                waitingConsumer = null;
                                return;
                            }
                            // Infinite stream completed - reconnect just as we do on a transient drop
                            failedTryCount++;
                        }
                        catch (e) {
                            if (isAborted())
                                return;

                            failedTryCount++;
                            if (maxRetries !== undefined && failedTryCount >= maxRetries) {
                                error = e;
                                done = true;
                                waitingConsumer?.resolve(undefined);
                                waitingConsumer = null;
                                return;
                            }

                            warnLog?.log(`retry(${failedTryCount}): error:`, e);
                        }

                        if (resetItem !== undefined) {
                            buffer.push(resetItem);
                            waitingConsumer?.resolve(undefined);
                            waitingConsumer = null;
                        }

                        const delay = retryDelays(failedTryCount - 1);
                        if (delay > 0)
                            await delayAsync(delay);
                    }
                }
                catch (e) {
                    error = e;
                    done = true;
                    waitingConsumer?.resolve(undefined);
                    waitingConsumer = null;
                }
            }

            // Abort listener
            if (signal) {
                void abortPromise(signal).catch((reason: unknown) => {
                    done = true;
                    error = reason;
                    waitingConsumer?.resolve(undefined);
                    waitingConsumer = null;
                });
            }

            return {
                async next(): Promise<IteratorResult<T>> {
                    while (buffer.length === 0 && !done) {
                        waitingConsumer = new PromiseSource<void>();
                        await waitingConsumer;
                    }

                    if (buffer.length > 0)
                        return { value: buffer.shift()!, done: false };

                    if (error)
                        // eslint-disable-next-line @typescript-eslint/only-throw-error
                        throw error;

                    return { value: undefined as T, done: true };
                },
            };
        },
    };
}

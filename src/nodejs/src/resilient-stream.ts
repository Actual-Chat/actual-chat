import { getLogs } from 'logging';
import { OperationCancelledError, cancelled, type Cancelled, PromiseSource, delayAsync } from 'promises';

const { warnLog } = getLogs('ResilientStream');

// --- Retry delay sequences ---

export type RetryDelayFn = (tryIndex: number) => number;

export function expRetryDelays(minMs: number, maxMs: number): RetryDelayFn {
    return (tryIndex: number) => Math.min(minMs * Math.pow(2, tryIndex), maxMs);
}

// --- Options ---

export interface ResilientStreamOptions<T> {
    provider: (cancel?: Promise<Cancelled>) => AsyncIterable<T>;
    resetItem?: T;
    retryDelays?: RetryDelayFn;
    maxRetries?: number; // undefined = infinite
    cancel?: Promise<Cancelled>;
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
        cancel,
    } = options;

    return {
        [Symbol.asyncIterator](): AsyncIterator<T> {
            const buffer: T[] = [];
            let done = false;
            let error: unknown = undefined;
            let waitingConsumer: PromiseSource<void> | null = null;
            let cancelled_ = false;

            // Start the background producer
            void pushItems();

            async function pushItems(): Promise<void> {
                let failedTryCount = 0;
                try {
                    for (;;) {
                        try {
                            const source = provider(cancel);
                            for await (const item of source) {
                                if (cancelled_) return;
                                buffer.push(item);
                                waitingConsumer?.resolve(undefined);
                                waitingConsumer = null;
                            }
                            // Normal completion
                            done = true;
                            waitingConsumer?.resolve(undefined);
                            waitingConsumer = null;
                            return;
                        }
                        catch (e) {
                            if (cancelled_ || e instanceof OperationCancelledError)
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
                }
                catch (e) {
                    error = e;
                    done = true;
                    waitingConsumer?.resolve(undefined);
                    waitingConsumer = null;
                }
            }

            // Cancel listener
            void cancel?.then(v => {
                if (v === cancelled) {
                    cancelled_ = true;
                    done = true;
                    error = new OperationCancelledError();
                    waitingConsumer?.resolve(undefined);
                    waitingConsumer = null;
                }
            });

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

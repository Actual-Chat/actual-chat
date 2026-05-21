// Local gateway to ix (IxJS) — re-exports the subset we use plus
// `fromReadable` / `logItems` (no direct ix equivalent). Reach for ix
// through this module; add re-exports here when you need new ones.
//
// `PipeOperator<TIn, TOut>` is `(AsyncIterable<TIn>) => AsyncIterableX<TOut>`;
// wrap async generators with `from(...)` so the return type matches.
// Compose with `pipe(source, op1, op2, …)`; drain with `await drain(...)`
// or `count(...)`.

import { AsyncIterableX } from 'ix/asynciterable/asynciterablex';
import type { OperatorAsyncFunction, MonoTypeOperatorAsyncFunction } from 'ix/interfaces';

// ---- ix re-exports ------------------------------------------------------

export { AsyncIterableX };
export type {
    OperatorFunction,
    MonoTypeOperatorFunction,
    UnaryFunction,
} from 'ix/interfaces';

// Re-export of ix's `OperatorAsyncFunction` under a project-friendly name.
export type PipeOperator<TIn, TOut> = OperatorAsyncFunction<TIn, TOut>;
export type MonoTypePipeOperator<T> = MonoTypeOperatorAsyncFunction<T>;

// Async-iterable producers / terminal operations.
export { count } from 'ix/asynciterable/count';
export { every } from 'ix/asynciterable/every';
export { first } from 'ix/asynciterable/first';
export { last } from 'ix/asynciterable/last';
export { some } from 'ix/asynciterable/some';
export { toArray } from 'ix/asynciterable/toarray';
export { reduce } from 'ix/asynciterable/reduce';

// Operators (most-used subset; add as needed).
export { catchError } from 'ix/asynciterable/operators/catcherror';
export { distinctUntilChanged } from 'ix/asynciterable/operators/distinctuntilchanged';
export { filter } from 'ix/asynciterable/operators/filter';
export { finalize } from 'ix/asynciterable/operators/finalize';
export { flatMap } from 'ix/asynciterable/operators/flatmap';
export { map } from 'ix/asynciterable/operators/map';
export { scan } from 'ix/asynciterable/operators/scan';
export { skip } from 'ix/asynciterable/operators/skip';
export { skipUntil } from 'ix/asynciterable/operators/skipuntil';
export { skipWhile } from 'ix/asynciterable/operators/skipwhile';
export { startWith } from 'ix/asynciterable/operators/startwith';
export { take } from 'ix/asynciterable/operators/take';
export { takeUntil } from 'ix/asynciterable/operators/takeuntil';
export { takeWhile } from 'ix/asynciterable/operators/takewhile';
export { tap } from 'ix/asynciterable/operators/tap';
export { withAbort } from 'ix/asynciterable/operators/withabort';

// Aliases `AsyncIterableX.from` — wraps a plain `AsyncIterable<T>` so
// it satisfies `PipeOperator`'s return type.
export function from<T>(source: AsyncIterable<T>): AsyncIterableX<T> {
    return AsyncIterableX.from(source);
}

// ---- pipe ---------------------------------------------------------------

// Free-function `pipe` — same as `from(source).pipe(...ops)` but
// reads better when the source is a plain `AsyncIterable`.
export function pipe<A, B>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
): AsyncIterableX<B>;
export function pipe<A, B, C>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
    op2: PipeOperator<B, C>,
): AsyncIterableX<C>;
export function pipe<A, B, C, D>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
    op2: PipeOperator<B, C>,
    op3: PipeOperator<C, D>,
): AsyncIterableX<D>;
export function pipe<A, B, C, D, E>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
    op2: PipeOperator<B, C>,
    op3: PipeOperator<C, D>,
    op4: PipeOperator<D, E>,
): AsyncIterableX<E>;
export function pipe<A, B, C, D, E, F>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
    op2: PipeOperator<B, C>,
    op3: PipeOperator<C, D>,
    op4: PipeOperator<D, E>,
    op5: PipeOperator<E, F>,
): AsyncIterableX<F>;
export function pipe<A, B, C, D, E, F, G>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
    op2: PipeOperator<B, C>,
    op3: PipeOperator<C, D>,
    op4: PipeOperator<D, E>,
    op5: PipeOperator<E, F>,
    op6: PipeOperator<F, G>,
): AsyncIterableX<G>;
export function pipe<A, B, C, D, E, F, G, H>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
    op2: PipeOperator<B, C>,
    op3: PipeOperator<C, D>,
    op4: PipeOperator<D, E>,
    op5: PipeOperator<E, F>,
    op6: PipeOperator<F, G>,
    op7: PipeOperator<G, H>,
): AsyncIterableX<H>;
export function pipe<A, B, C, D, E, F, G, H, I>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
    op2: PipeOperator<B, C>,
    op3: PipeOperator<C, D>,
    op4: PipeOperator<D, E>,
    op5: PipeOperator<E, F>,
    op6: PipeOperator<F, G>,
    op7: PipeOperator<G, H>,
    op8: PipeOperator<H, I>,
): AsyncIterableX<I>;
export function pipe<A, B, C, D, E, F, G, H, I, J>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
    op2: PipeOperator<B, C>,
    op3: PipeOperator<C, D>,
    op4: PipeOperator<D, E>,
    op5: PipeOperator<E, F>,
    op6: PipeOperator<F, G>,
    op7: PipeOperator<G, H>,
    op8: PipeOperator<H, I>,
    op9: PipeOperator<I, J>,
): AsyncIterableX<J>;
export function pipe<A, B, C, D, E, F, G, H, I, J, K>(
    source: AsyncIterable<A>,
    op1: PipeOperator<A, B>,
    op2: PipeOperator<B, C>,
    op3: PipeOperator<C, D>,
    op4: PipeOperator<D, E>,
    op5: PipeOperator<E, F>,
    op6: PipeOperator<F, G>,
    op7: PipeOperator<G, H>,
    op8: PipeOperator<H, I>,
    op9: PipeOperator<I, J>,
    op10: PipeOperator<J, K>,
): AsyncIterableX<K>;
export function pipe(
    source: AsyncIterable<unknown>,
    ...operators: PipeOperator<unknown, unknown>[]
): AsyncIterableX<unknown> {
    return operators.reduce<AsyncIterableX<unknown>>(
        (current, op) => op(current),
        AsyncIterableX.from(source),
    );
}

// ---- drain --------------------------------------------------------------

// Run to completion. Same as `count` from ix when the count is uninteresting.
// If `shouldSilenceError` is supplied, matching errors are treated as normal
// completion. This is useful for terminal pipeline drains where a caller-owned
// abort is an expected safety path during shutdown.
export async function drain(
    source: AsyncIterable<unknown>,
    shouldSilenceError?: (error: unknown) => boolean,
): Promise<void> {
    try {
        for await (const _ of source) { void _; }
    } catch (e) {
        if (shouldSilenceError?.(e) === true)
            return;
        throw e;
    }
}

// ---- exclusive ----------------------------------------------------------

// A second concurrent iteration throws; sequential reuse (after the
// prior iteration's `finally` runs) is allowed.
export function exclusive<T>(
    source: AsyncIterable<T>,
    message = 'Async iterable is already running.',
): AsyncIterableX<T> {
    let active = false;
    return AsyncIterableX.from({
        async *[Symbol.asyncIterator](): AsyncGenerator<T, void, unknown> {
            if (active) throw new Error(message);
            active = true;
            try {
                for await (const item of source) {
                    yield item;
                }
            } finally {
                active = false;
            }
        },
    });
}

// ---- fromReadable -------------------------------------------------------

/**
 * Wrap a `ReadableStream<T>` as an `AsyncIterableX<T>` with explicit
 * cancel-vs-release-lock control. The native
 * `ReadableStream[Symbol.asyncIterator]` owns the lock; we want to
 * decide ourselves whether early-return cancels or just releases.
 *
 * `closeOnReturn` (default `true`): on return / break / throw, cancel
 * the reader (tears down the upstream source). Set `false` when the
 * stream is shared / cached and another consumer will re-iterate it.
 */
export interface FromReadableOptions {
    closeOnReturn?: boolean;
    abortSignal?: AbortSignal;
}

export function fromReadable<T>(
    stream: ReadableStream<T>,
    opts: FromReadableOptions = {},
): AsyncIterableX<T> {
    return AsyncIterableX.from(impl());

    async function* impl(): AsyncIterable<T> {
        const { closeOnReturn = true, abortSignal } = opts;
        const reader = stream.getReader();
        const abort = (): void => {
            void reader.cancel(abortSignal?.reason).catch(() => { /* ignore */ });
        };
        if (abortSignal?.aborted)
            abort();
        else
            abortSignal?.addEventListener('abort', abort, { once: true });
        try {
            for (;;) {
                const result = await reader.read();
                if (result.done)
                    return;

                yield result.value;
            }
        } finally {
            abortSignal?.removeEventListener('abort', abort);
            try {
                if (closeOnReturn) {
                    await reader.cancel();
                }
            } catch { /* ignore */ }
            try { reader.releaseLock(); } catch { /* ignore */ }
        }
    }
}

// ---- logItems ----------------------------------------------------------

// Diagnostic passthrough: logs a counted line per item, dense for the
// first `firstN` then every `everyN`th. Tag is grep-able as `[pipe X]`.
export interface LogItemsOptions<T> {
    /** Default 5. */
    firstN?: number;
    /** Default 60. */
    everyN?: number;
    /** Default `console.warn`. */
    log?: (line: string) => void;
    /** Extra context appended after the count. */
    format?: (item: T, count: number) => string;
}

export function logItems<T>(
    tag: string,
    opts: LogItemsOptions<T> = {},
): PipeOperator<T, T> {
    const firstN = opts.firstN ?? 5;
    const everyN = opts.everyN ?? 60;
    const log = opts.log ?? ((line: string) => { console.warn(line); });
    const format = opts.format;
    return source => AsyncIterableX.from((async function* () {
        let count = 0;
        for await (const item of source) {
            count++;
            if (count <= firstN || count % everyN === 0) {
                try {
                    const extra = format ? ` ${format(item, count)}` : '';
                    log(`[pipe ${tag}] #${count}${extra}`);
                } catch (e) {
                    console.warn(`[pipe ${tag}] log failed:`, e);
                }
            }
            yield item;
        }
    })());
}

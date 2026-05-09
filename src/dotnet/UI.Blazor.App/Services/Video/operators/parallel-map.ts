import { from, type PipeOperator } from 'ix-ext';

// Ordered parallel-map: up to `concurrency` map calls run simultaneously while
// output order matches input arrival. On mapper error, in-flight items finish
// (results yielded or routed to onUnconsumedResult) before the rethrow.
// Backpressure: only pulls upstream when a slot is free.
export interface ParallelMapOptions<TIn, TOut> {
    concurrency: number;
    map: (input: TIn, slotId: number) => TOut | Promise<TOut>;
    // Lazy: invoked just before the slot's first map call.
    onSlotInit?: (slotId: number) => void;
    // Invoked at teardown, highest slot first.
    onSlotDispose?: (slotId: number) => void;
    // Invoked on outputs that were produced but cannot be yielded (downstream
    // stopped or operator threw); lets the caller close TOut resources.
    onUnconsumedResult?: (output: TOut) => void;
}

interface PendingItem<TOut> {
    seq: number;
    slotId: number;
    promise: Promise<TOut>;
}

export function parallelMap<TIn, TOut>(opts: ParallelMapOptions<TIn, TOut>): PipeOperator<TIn, TOut> {
    if (!Number.isFinite(opts.concurrency) || opts.concurrency < 1)
        throw new Error('parallelMap: concurrency must be ≥ 1');
    const concurrency = Math.floor(opts.concurrency);
    const { map, onSlotInit, onSlotDispose, onUnconsumedResult } = opts;

    return source => from(impl(source));

    async function* impl(source: AsyncIterable<TIn>): AsyncIterable<TOut> {
        const slotInitialized = new Array<boolean>(concurrency).fill(false);
        const slotBusy = new Array<boolean>(concurrency).fill(false);
        const pending: PendingItem<TOut>[] = [];
        // Keyed by submission sequence so we can yield in source order.
        const settled = new Map<number, { ok: true; value: TOut } | { ok: false; error: unknown }>();
        let nextSeqIn = 0;
        let nextSeqOut = 0;
        let sourceDone = false;
        let firstError: unknown = null;

        // Latching wake-signal: avoids re-racing already-resolved promises (which would busy-loop).
        let wakePending = false;
        let wakeResolve: (() => void) | null = null;
        const signalWake = (): void => {
            if (wakeResolve) {
                const r = wakeResolve;
                wakeResolve = null;
                r();
            } else {
                wakePending = true;
            }
        };
        const waitForWake = (): Promise<void> => {
            if (wakePending) {
                wakePending = false;
                return Promise.resolve();
            }
            return new Promise<void>(resolve => { wakeResolve = resolve; });
        };

        const iterator = source[Symbol.asyncIterator]();

        const acquireSlot = (): number => {
            for (let i = 0; i < concurrency; i++) {
                if (!slotBusy[i]) return i;
            }
            return -1;
        };

        const dispatch = async (slotId: number, input: TIn): Promise<TOut> => {
            slotBusy[slotId] = true;
            if (!slotInitialized[slotId]) {
                slotInitialized[slotId] = true;
                if (onSlotInit) onSlotInit(slotId);
            }
            try {
                return await map(input, slotId);
            } finally {
                slotBusy[slotId] = false;
            }
        };

        try {
            while (!sourceDone || pending.length > 0) {
                while (!sourceDone && pending.length < concurrency && firstError === null) {
                    const slotId = acquireSlot();
                    if (slotId < 0) break;
                    let nextResult: IteratorResult<TIn>;
                    try {
                        nextResult = await iterator.next();
                    } catch (e) {
                        firstError = e;
                        sourceDone = true;
                        break;
                    }
                    if (nextResult.done) {
                        sourceDone = true;
                        break;
                    }
                    const seq = nextSeqIn++;
                    const promise = dispatch(slotId, nextResult.value);
                    pending.push({ seq, slotId, promise });
                    void promise.then(
                        value => { settled.set(seq, { ok: true, value }); signalWake(); },
                        (error: unknown) => { settled.set(seq, { ok: false, error }); signalWake(); },
                    );
                }

                while (settled.has(nextSeqOut)) {
                    const result = settled.get(nextSeqOut)!;
                    settled.delete(nextSeqOut);
                    const idx = pending.findIndex(p => p.seq === nextSeqOut);
                    if (idx >= 0) pending.splice(idx, 1);
                    nextSeqOut++;
                    if (result.ok) {
                        if (firstError !== null) {
                            // Already failed: close instead of yielding so the resource doesn't escape unowned.
                            if (onUnconsumedResult) onUnconsumedResult(result.value);
                        } else {
                            yield result.value;
                        }
                    } else {
                        if (firstError === null) firstError = result.error;
                    }
                }

                if (firstError !== null && pending.length === 0)
                    break;

                if (sourceDone && pending.length === 0)
                    break;

                if (pending.length === 0)
                    continue;

                await waitForWake();
            }
        } finally {
            // Drain still-settling tasks through onUnconsumedResult instead of orphaning them.
            const drainPromises = pending.map(p => p.promise.then(
                v => ({ ok: true as const, value: v }),
                (e: unknown) => ({ ok: false as const, error: e }),
            ));
            const drained = await Promise.all(drainPromises);
            for (const r of drained) {
                if (r.ok && onUnconsumedResult) onUnconsumedResult(r.value);
            }
            for (let i = concurrency - 1; i >= 0; i--) {
                if (slotInitialized[i] && onSlotDispose) {
                    try { onSlotDispose(i); } catch { /* ignore */ }
                }
            }
            try { await iterator.return?.(); } catch { /* ignore */ }
        }

        if (firstError !== null) throw firstError as Error;
    }
}

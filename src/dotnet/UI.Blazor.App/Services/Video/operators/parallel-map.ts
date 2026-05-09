import { from, type PipeOperator } from 'ix-ext';

/**
 * Ordered parallel-map operator. Up to `concurrency` `map` calls run
 * simultaneously while output ordering matches input arrival order.
 *
 * Each in-flight call is dispatched to a numbered slot (0..concurrency-1).
 * `onSlotInit` / `onSlotDispose` are called lazily — when the slot is
 * first used and at operator teardown. Useful for per-slot resource
 * allocation (e.g. one downscaler instance per slot).
 *
 * On error: the rejected mapper's exception is rethrown after all
 * concurrently in-flight items finish. Their results are still emitted
 * (or, if downstream stops consuming, closed via `onUnconsumedResult`)
 * before the throw — this keeps ownership of `TOut` instances explicit.
 *
 * Backpressure: the operator pulls more from upstream only when a slot
 * is free, so a slow downstream consumer naturally bounds in-flight
 * count to `concurrency`.
 */
export interface ParallelMapOptions<TIn, TOut> {
    /** Max concurrent mapper calls. Must be ≥ 1. */
    concurrency: number;
    /** Per-item transform; receives a slot id ∈ [0, concurrency-1] for
     *  per-slot resource binding. */
    map: (input: TIn, slotId: number) => Promise<TOut>;
    /** Called once per slot just before its first `map` call. */
    onSlotInit?: (slotId: number) => void;
    /** Called once per slot at operator teardown (normal completion or
     *  abort). Order: highest slot first. */
    onSlotDispose?: (slotId: number) => void;
    /** Called on outputs that were produced but cannot be yielded
     *  (downstream stopped, operator threw). Lets the caller close
     *  GPU/codec resources owned by `TOut`. */
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
        // Map<seq, settled> — completed but not-yet-yielded results,
        // keyed by submission sequence so we can yield in source order.
        const settled = new Map<number, { ok: true; value: TOut } | { ok: false; error: unknown }>();
        let nextSeqIn = 0;
        let nextSeqOut = 0;
        let sourceDone = false;
        let firstError: unknown = null;

        // Latching wake-signal: any task settling sets it; the main loop
        // consumes it and re-creates the gate. Avoids re-racing already-
        // resolved promises (which would busy-loop).
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

        const dispatch = async (slotId: number, input: TIn, seq: number): Promise<TOut> => {
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
            // Pump: keep slots full until the source completes AND all
            // pending tasks have been awaited.
            while (!sourceDone || pending.length > 0) {
                // Fill open slots from the source.
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
                    const promise = dispatch(slotId, nextResult.value, seq);
                    pending.push({ seq, slotId, promise });
                    // Settle in the background so multiple in-flight tasks
                    // can complete out of order, then signal the main loop.
                    void promise.then(
                        value => { settled.set(seq, { ok: true, value }); signalWake(); },
                        error => { settled.set(seq, { ok: false, error }); signalWake(); },
                    );
                }

                // Anything ready to yield (in order)?
                while (settled.has(nextSeqOut)) {
                    const result = settled.get(nextSeqOut)!;
                    settled.delete(nextSeqOut);
                    // Drop the corresponding pending entry.
                    const idx = pending.findIndex(p => p.seq === nextSeqOut);
                    if (idx >= 0) pending.splice(idx, 1);
                    nextSeqOut++;
                    if (result.ok) {
                        if (firstError !== null) {
                            // Already failed; close instead of yielding so
                            // the resource doesn't escape unowned.
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

                if (pending.length === 0) {
                    // No tasks in flight but source isn't done — re-fill.
                    continue;
                }

                // Wait for at least one in-flight to settle. The wake gate
                // is the only signal; we never re-race already-resolved
                // promises here.
                await waitForWake();
            }
        } finally {
            // Drain any still-settling tasks so we can close their results
            // through onUnconsumedResult instead of orphaning them.
            const drainPromises = pending.map(p => p.promise.then(
                v => ({ ok: true as const, value: v }),
                e => ({ ok: false as const, error: e }),
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

        if (firstError !== null) throw firstError;
    }
}

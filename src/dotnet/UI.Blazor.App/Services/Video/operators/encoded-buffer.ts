import { from, type PipeOperator } from 'ix-ext';
import { abortPromise } from 'actuallab-core';
import { closeEncodedChunk, type ArrivedChunk } from '../frame-envelopes';
import type { EncodedFrameBuffer } from '../playback/encoded-frame-buffer';

export interface PacedEncodedBufferOptions {
    // Shared with epoch-reset.ts: that operator owns reset(), this one owns push/drain.
    buffer: EncodedFrameBuffer;
    abortSignal?: AbortSignal;
}

// Receiver-side drain for an EncodedFrameBuffer.
//
// Upstream pull runs EAGERLY in its own producer loop, decoupled from the
// present pace: it drains whatever the server delivers into the buffer as fast
// as the RpcStream yields it. A consumer that subscribed behind the live edge
// (replay-tail keyframe) thus pulls the server's backlog into the buffer, where
// EncodedFrameBuffer.trySkipToLive (on push) sheds it to the newest keyframe —
// instead of trailing the server forever at 1× (which is what a present-paced
// pull does). The consumer side yields due frames at the present pace.
export function pacedEncodedBuffer(opts: PacedEncodedBufferOptions): PipeOperator<ArrivedChunk, ArrivedChunk> {
    const { buffer, abortSignal } = opts;
    const abortRace: Promise<never> = abortSignal
        ? abortPromise(abortSignal)
        : new Promise(() => { /* never resolves */ });
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<ArrivedChunk> {
            const iterator = source[Symbol.asyncIterator]();
            // Mutated by the producer, read by the consumer — a holder object so the
            // cross-closure writes are visible (a captured `let` would narrow to its
            // initializer in the consumer).
            const pump: { done: boolean; error: unknown } = { done: false, error: undefined };
            let notify: (() => void) | null = null;
            const wake = (): void => { const n = notify; notify = null; n?.(); };

            const produce = async (): Promise<void> => {
                let pendingNext: Promise<IteratorResult<ArrivedChunk>> | null = null;
                try {
                    while (!abortSignal?.aborted) {
                        pendingNext = iterator.next();
                        let result: IteratorResult<ArrivedChunk>;
                        try {
                            result = await Promise.race([pendingNext, abortRace]);
                        } catch {
                            break; // aborted
                        }
                        pendingNext = null;
                        if (result.done)
                            return;

                        const chunk = result.value;
                        let mustClose = true;
                        try {
                            buffer.push(chunk);
                            mustClose = false;
                        } finally {
                            if (mustClose)
                                closeEncodedChunk(chunk.chunk);
                        }
                        wake();
                    }
                } catch (e) {
                    if (!abortSignal?.aborted)
                        pump.error = e;
                } finally {
                    pump.done = true;
                    wake();
                    if (typeof iterator.return === 'function') {
                        try { await iterator.return(undefined as never); } catch { /* ignore */ }
                    }
                    if (pendingNext !== null) {
                        // Tail handler (not awaited): closes the chunk satisfying the in-flight
                        // next() so a non-cooperative upstream can't leak it.
                        pendingNext.then(
                            r => { if (!r.done) closeEncodedChunk(r.value.chunk); },
                            () => { /* ignore */ },
                        );
                    }
                }
            };
            const producer = produce();

            try {
                while (!abortSignal?.aborted) {
                    // Arm the wakeup BEFORE draining/checking so a push that lands
                    // during this iteration can't be lost between check and await.
                    const changed = new Promise<void>(resolve => { notify = resolve; });
                    let drained: ArrivedChunk | null;
                    while ((drained = buffer.tryPull()) !== null) {
                        let mustClose = true;
                        try {
                            mustClose = false;
                            yield drained;
                        } finally {
                            if (mustClose)
                                closeEncodedChunk(drained.chunk);
                        }
                    }
                    if (pump.error !== undefined)
                        throw pump.error instanceof Error ? pump.error : new Error('pacedEncodedBuffer: upstream error');
                    // Upstream ended: drain is exhausted above, trailing sub-target
                    // frames (if any) are closed by reset() in finally — same as the
                    // legacy single-loop behavior.
                    if (pump.done)
                        return;

                    try {
                        await Promise.race([changed, abortRace]);
                    } catch {
                        // Aborted: the while-condition exits the loop and finally
                        // unwinds cleanly (real upstream errors surface via pump.error).
                    }
                }
            } finally {
                await producer.catch(() => { /* ignore */ });
                buffer.reset();
            }
        }
    };
}

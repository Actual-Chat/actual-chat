import { from, type PipeOperator } from 'ix-ext';
import { abortPromise } from 'promises';
import { closeEncodedChunk, type ArrivedChunk } from '../frame-envelopes';
import type { EncodedFrameBuffer } from '../playback/encoded-frame-buffer';

export interface PacedEncodedBufferOptions {
    /** Shared with `epoch-reset.ts`: that operator owns `reset()`,
     *  this one owns push/drain. */
    buffer: EncodedFrameBuffer;
    abortSignal?: AbortSignal;
}

/**
 * Receiver-side drain for an `EncodedFrameBuffer`. Pushes each input
 * chunk into the buffer, yields whatever is currently due, and waits
 * for the next upstream arrival to trigger a re-evaluation.
 *
 * No internal pacing — the buffer's `isReady()` is purely a function of
 * `spanMs() >= targetSpanMs`, so span only changes on push (or pull).
 * Downstream backpressure (the present stage's 60 fps cap, plus its
 * catch-up skip policy when the buffer overflows) governs how fast the
 * inner drain loop yields. Drops at the buffer (deltas pushed while
 * reset-armed) bump `stats.chunksDroppedAtBuffer`.
 */
export function pacedEncodedBuffer(opts: PacedEncodedBufferOptions): PipeOperator<ArrivedChunk, ArrivedChunk> {
    const { buffer, abortSignal } = opts;
    const abortRace: Promise<never> = abortSignal
        ? abortPromise(abortSignal)
        : new Promise(() => { /* never resolves */ });
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<ArrivedChunk> {
            const iterator = source[Symbol.asyncIterator]();
            // `pendingNext` survives across drain iterations so we don't
            // ask the upstream iterator twice for the same item.
            let pendingNext: Promise<IteratorResult<ArrivedChunk>> | null = null;
            try {
                while (!abortSignal?.aborted) {
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
                    pendingNext = iterator.next();
                    const result = await Promise.race([pendingNext, abortRace]);
                    pendingNext = null;
                    if (result.done)
                        return;

                    const chunk = result.value;
                    let mustClose = true;
                    try {
                        const status = buffer.push(chunk);
                        mustClose = false;
                        if (status === 'droppedReset')
                            chunk.stats.chunksDroppedAtBuffer++;
                    } finally {
                        if (mustClose)
                            closeEncodedChunk(chunk.chunk);
                    }
                }
            } finally {
                // Drop retained chunks; anything already yielded is downstream's.
                buffer.reset();
                if (pendingNext !== null) {
                    // The chunk that satisfies an in-flight next() doesn't reach
                    // the buffer, so without an explicit close hook it leaks.
                    // Attached as a tail handler (not awaited) so a non-cooperative
                    // upstream that never settles return() can't block teardown.
                    pendingNext.then(
                        r => { if (!r.done) closeEncodedChunk(r.value.chunk); },
                        () => { /* ignore */ },
                    );
                    if (typeof iterator.return === 'function') {
                        try { await iterator.return(undefined as never); } catch { /* ignore */ }
                    }
                    pendingNext = null;
                }
            }
        }
    };
}

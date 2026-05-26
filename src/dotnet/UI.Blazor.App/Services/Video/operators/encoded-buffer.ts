import { from, type PipeOperator } from 'ix-ext';
import { abortPromise } from 'actuallab-core';
import { closeEncodedChunk, type ArrivedChunk } from '../frame-envelopes';
import type { EncodedFrameBuffer } from '../playback/encoded-frame-buffer';

export interface PacedEncodedBufferOptions {
    // Shared with epoch-reset.ts: that operator owns reset(), this one owns push/drain.
    buffer: EncodedFrameBuffer;
    abortSignal?: AbortSignal;
}

// Receiver-side drain for an EncodedFrameBuffer. Pushes each input chunk,
// yields whatever is currently due, then waits for the next upstream arrival.
export function pacedEncodedBuffer(opts: PacedEncodedBufferOptions): PipeOperator<ArrivedChunk, ArrivedChunk> {
    const { buffer, abortSignal } = opts;
    const abortRace: Promise<never> = abortSignal
        ? abortPromise(abortSignal)
        : new Promise(() => { /* never resolves */ });
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<ArrivedChunk> {
            const iterator = source[Symbol.asyncIterator]();
            // Survives across drain iterations so we don't ask upstream twice for the same item.
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
                        buffer.push(chunk);
                        mustClose = false;
                    } finally {
                        if (mustClose)
                            closeEncodedChunk(chunk.chunk);
                    }
                }
            } finally {
                buffer.reset();
                if (pendingNext !== null) {
                    // Tail handler (not awaited): closes the chunk satisfying the in-flight next()
                    // so a non-cooperative upstream that never settles return() can't leak it.
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

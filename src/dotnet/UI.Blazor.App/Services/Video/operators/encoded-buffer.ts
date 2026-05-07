import { from, type PipeOperator } from 'ix-ext';
import { abortPromise } from 'promises';
import { closeEncodedChunk, type ArrivedChunk } from '../frame-envelopes';
import type { EncodedFrameBuffer } from '../playback/encoded-frame-buffer';

export interface PacedEncodedBufferOptions {
    /** Shared with `epoch-reset.ts`: that operator owns `reset()`,
     *  this one owns push/drain. */
    buffer: EncodedFrameBuffer;
    /** Test seam (passed through to `EncodedFrameBuffer.now`). */
    now?: () => number;
    /** Test seam for `setTimeout`. Production uses platform default. */
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
    abortSignal?: AbortSignal;
}

/**
 * Receiver-side paced drain. Pushes each input chunk into the buffer,
 * yields whatever is currently due, and races upstream-arrival vs. the
 * front chunk's pacing deadline when nothing is due yet.
 *
 * Pacing exists so a network burst (multiple chunks delivered together)
 * doesn't drain the cushion in one tick. Drops at the buffer (deltas
 * pushed while reset-armed) bump `stats.chunksDroppedAtBuffer`.
 */
export function pacedEncodedBuffer(opts: PacedEncodedBufferOptions): PipeOperator<ArrivedChunk, ArrivedChunk> {
    const { buffer, abortSignal } = opts;
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms) => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn ?? ((h) => clearTimeout(h as ReturnType<typeof setTimeout>));
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
                    const next = pendingNext ?? (pendingNext = iterator.next());
                    if (buffer.count() > 0) {
                        const result = await raceWithTimeout(
                            next,
                            computeNextDueDelayMs(buffer),
                            setTimeoutFn,
                            clearTimeoutFn,
                            abortRace,
                        );
                        if (result === 'timeout')
                            continue;

                        pendingNext = null;
                        if (result.done)
                            return;

                        const { value: chunk } = result;
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
                        continue;
                    }
                    const result = await Promise.race([next, abortRace]);
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
                if (pendingNext !== null && typeof iterator.return === 'function') {
                    try { await iterator.return(undefined as never); } catch { /* ignore */ }
                }
            }
        }
    };
}

// We can't peek at the buffer's front chunk through the public API; just
// short-circuit when due, otherwise re-check after a small slice.
function computeNextDueDelayMs(buffer: EncodedFrameBuffer): number {
    return buffer.isReady() ? 0 : 5;
}

type RaceResult<T> = 'timeout' | IteratorResult<T>;

async function raceWithTimeout<T>(
    pending: Promise<IteratorResult<T>>,
    delayMs: number,
    setTimeoutFn: (cb: () => void, ms: number) => unknown,
    clearTimeoutFn: (handle: unknown) => void,
    aborted: Promise<never>,
): Promise<RaceResult<T>> {
    let timerHandle: unknown = null;
    const timer = new Promise<'timeout'>(resolve => {
        timerHandle = setTimeoutFn(() => resolve('timeout'), delayMs);
    });
    try {
        return await Promise.race([pending, timer, aborted]);
    } finally {
        if (timerHandle !== null) {
            try { clearTimeoutFn(timerHandle); } catch { /* ignore */ }
        }
    }
}

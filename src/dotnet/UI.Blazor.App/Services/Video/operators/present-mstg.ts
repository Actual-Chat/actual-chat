import { from, type PipeOperator } from 'ix-ext';
import type { DecodedFrame } from '../frame-envelopes';

// Production binding: the writable of a `MediaStreamTrackGenerator`
// whose track is rendered via `<video>.srcObject`.
export interface MstgPresentOptions {
    getWriter: () => WritableStreamDefaultWriter<VideoFrame>;
}

// Bundled so input + write loops share state through one ref (sidesteps
// `no-unnecessary-condition` surprises with `let writeInFlight = false`).
interface MstgState {
    pending: DecodedFrame | null;
    inFlight: DecodedFrame | null;
    busy: boolean;
}

/**
 * Terminal sink: single-slot replace into a `MediaStreamTrackGenerator`.
 * At most one *pending* envelope plus optionally one *in-flight* write
 * — fresher arrivals replace the pending slot, since the receiver
 * always wants the latest frame.
 *
 * `framesPresented` counts real writes only; frames that lost the slot
 * to a fresher arrival are not counted.
 */
export function mstgPresent(opts: MstgPresentOptions): PipeOperator<DecodedFrame, void> {
    const { getWriter } = opts;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<void> {
            const state: MstgState = { pending: null, inFlight: null, busy: false };
            let writer: WritableStreamDefaultWriter<VideoFrame> | null = null;
            let writeLoopDone: Promise<void> = Promise.resolve();
            let completedNormally = false;
            const startWriteLoop = (): void => {
                writeLoopDone = runWriteLoop();
                // Keep the promise observed until the input loop reaches a
                // point where it can propagate the failure.
                writeLoopDone.catch(() => { /* observed below / on drain */ });
            };
            const runWriteLoop = async (): Promise<void> => {
                while (state.pending !== null) {
                    const envelope = state.pending;
                    state.pending = null;
                    state.busy = true;
                    state.inFlight = envelope;
                    try {
                        await writer!.write(envelope.frame);
                        envelope.stats.framesPresented++;
                    } finally {
                        try { envelope.frame.close(); } catch { /* already closed */ }
                        state.inFlight = null;
                        state.busy = false;
                    }
                }
            };
            try {
                for await (const decoded of source) {
                    let mustClose = true;
                    try {
                        writer ??= getWriter();
                        if (!state.busy)
                            await writeLoopDone;
                        // Close the previous pending frame; the write loop closes
                        // the in-flight frame when its write settles.
                        if (state.pending !== null) {
                            try { state.pending.frame.close(); } catch { /* already closed */ }
                        }
                        state.pending = decoded;
                        mustClose = false;
                        if (!state.busy)
                            startWriteLoop();
                    } finally {
                        if (mustClose)
                            try { decoded.frame.close(); } catch { /* already closed */ }
                    }
                }
                completedNormally = true;
                // Drain so the final frame's stats increment lands before return.
                await writeLoopDone;
            } finally {
                if (state.pending !== null) {
                    try { state.pending.frame.close(); } catch { /* already closed */ }
                    state.pending = null;
                }
                if (!completedNormally && state.inFlight !== null) {
                    try { state.inFlight.frame.close(); } catch { /* already closed */ }
                    state.inFlight = null;
                }
                if (completedNormally) {
                    try { await writeLoopDone; } catch { /* ignore */ }
                }
            }
        }
    };
}

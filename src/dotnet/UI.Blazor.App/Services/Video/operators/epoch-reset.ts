import { from, type PipeOperator } from 'ix-ext';
import { closeEncodedChunk, type ArrivedChunk } from '../frame-envelopes';
import type { EncodedFrameBuffer } from '../playback/encoded-frame-buffer';

export interface EpochResetOptions {
    // Shared with pacedEncodedBuffer: this operator owns reset(), the buffer operator owns push/drain.
    buffer: EncodedFrameBuffer;
}

// epoch === 0 is a valid producer value, so lastEpoch starts at null (not 0)
// — the first chunk anchors it instead of triggering a spurious reset.
export function resetOnEpochChange(opts: EpochResetOptions): PipeOperator<ArrivedChunk, ArrivedChunk> {
    const { buffer } = opts;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<ArrivedChunk> {
            let lastEpoch: number | null = null;
            for await (const chunk of source) {
                let mustClose = true;
                try {
                    const currentEpoch = chunk.capturedAt.epoch;
                    if (lastEpoch !== null && currentEpoch !== lastEpoch)
                        buffer.reset();

                    lastEpoch = currentEpoch;
                    mustClose = false;
                    yield chunk;
                } finally {
                    if (mustClose)
                        closeEncodedChunk(chunk.chunk);
                }
            }
        }
    };
}

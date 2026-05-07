import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

// Stamps `sourceWidth`/`sourceHeight` from the input frame's coded dims.
// Runs before downscale so wire keyframes carry original capture dims.
export function attachSourceDims(): PipeOperator<CapturedFrame, CapturedFrame> {
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            for await (const envelope of source) {
                let mustClose = true;
                try {
                    const output = {
                        ...envelope,
                        sourceWidth: envelope.frame.codedWidth,
                        sourceHeight: envelope.frame.codedHeight,
                    };
                    mustClose = false;
                    yield output;
                } finally {
                    if (mustClose)
                        try { envelope.frame.close(); } catch { /* ignore */ }
                }
            }
        }
    };
}

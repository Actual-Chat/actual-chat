import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

// Raises `forceKeyframe` when source coded dims change. Encoder
// reconfigure on dim change must hand the receiver a keyframe at the
// new dims; otherwise a following delta would decode garbage.
//
// Strictly raises — never lowers — the upstream flag (so the
// `stampCaptureTime` first-frame keyframe survives a no-dim-change frame).
export function forceKeyframeOnDimChange(): PipeOperator<CapturedFrame, CapturedFrame> {
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            let lastWidth = -1;
            let lastHeight = -1;
            for await (const envelope of source) {
                let mustClose = true;
                try {
                    const { codedWidth, codedHeight } = envelope.frame;
                    const dimsChanged = lastWidth !== -1
                        && (codedWidth !== lastWidth || codedHeight !== lastHeight);
                    lastWidth = codedWidth;
                    lastHeight = codedHeight;
                    if (dimsChanged && !envelope.forceKeyframe) {
                        const output = { ...envelope, forceKeyframe: true };
                        mustClose = false;
                        yield output;
                    } else {
                        mustClose = false;
                        yield envelope;
                    }
                } finally {
                    if (mustClose)
                        try { envelope.frame.close(); } catch { /* ignore */ }
                }
            }
        }
    };
}

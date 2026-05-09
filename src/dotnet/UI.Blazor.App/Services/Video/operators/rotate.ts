import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

export interface RotateOptions {
    // Called per frame so screen.orientation changes apply without restart.
    getRotationDeg: () => number;
}

// Sets VideoFrame.rotation (Chromium-only; Safari ignores it).
export function rotate(opts: RotateOptions): PipeOperator<CapturedFrame, CapturedFrame> {
    const { getRotationDeg } = opts;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            for await (const envelope of source) {
                let mustClose = true;
                try {
                    const rotationDeg = getRotationDeg();
                    try {
                        const f = envelope.frame as VideoFrame & { rotation?: number };
                        if (rotationDeg !== 0) f.rotation = rotationDeg;
                    } catch { /* hint, not load-bearing */ }
                    mustClose = false;
                    yield envelope;
                } finally {
                    if (mustClose)
                        try { envelope.frame.close(); } catch { /* ignore */ }
                }
            }
        }
    };
}

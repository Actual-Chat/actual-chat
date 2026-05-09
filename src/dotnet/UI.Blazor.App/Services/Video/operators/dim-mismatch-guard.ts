import { getLogs } from 'logging';
import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

const { warnLog } = getLogs('VideoPipeline');

export interface DimMismatchGuardOptions {
    // Called per frame so a downstream encoder reconfigure takes effect immediately.
    getExpectedDims: () => { width: number; height: number };
}

// Drops frames whose coded dims don't match the encoder's configured
// dims. Without this, Chrome's hardware encoders silently TOP-LEFT-CROP
// across reconfigure boundaries — multi-second visible artifact.
// Logs one warn per burst (cleared on first matching frame).
export function dropDimMismatch(opts: DimMismatchGuardOptions): PipeOperator<CapturedFrame, CapturedFrame> {
    const { getExpectedDims } = opts;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            let inMismatchBurst = false;
            for await (const envelope of source) {
                let mustClose = true;
                const frame = envelope.frame;
                try {
                    const { width, height } = getExpectedDims();
                    if (frame.codedWidth !== width || frame.codedHeight !== height) {
                        envelope.stats.framesDroppedDimMismatch++;
                        if (!inMismatchBurst) {
                            inMismatchBurst = true;
                            warnLog?.log(
                                `dropDimMismatch: frame=${frame.codedWidth}x${frame.codedHeight}, `
                                + `expected=${width}x${height} — dropping frame(s) until match`);
                        }
                        continue;
                    }
                    inMismatchBurst = false;
                    mustClose = false;
                    yield envelope;
                } finally {
                    if (mustClose)
                        try { frame.close(); } catch { /* ignore */ }
                }
            }
        }
    };
}

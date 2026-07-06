// Temporal downsampler driven by the recorder's fps policy (fps-policy.ts:
// thermal ceiling, thumbnail shed). `targetFps <= 0` drops every frame — the
// idle case where no viewer wants the stream, so we stop feeding the encoders
// (CPU ~0) while the camera track stays warm for instant resume. A non-finite
// target (the default) is a no-op: every frame passes. Like floodGate, it sits
// right after capture so paced-out frames release their GPU frame without
// traversing downscale/encode.

import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

const JITTER_EPS_MS = 4;

export class PaceState {
    private _targetFps = Number.POSITIVE_INFINITY;

    get targetFps(): number {
        return this._targetFps;
    }

    setTargetFps(fps: number): void {
        this._targetFps = fps;
    }
}

export function temporalPace(state: PaceState): PipeOperator<CapturedFrame, CapturedFrame> {
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            let nextDueMs: number | null = null;
            for await (const captured of source) {
                const fps = state.targetFps;
                if (fps <= 0) {
                    try { captured.frame.close(); } catch { /* ignore */ }
                    continue;
                }
                if (Number.isFinite(fps)) {
                    const intervalMs = 1000 / fps;
                    const nowMs = performance.now();
                    if (nextDueMs !== null && nowMs < nextDueMs - JITTER_EPS_MS) {
                        try { captured.frame.close(); } catch { /* ignore */ }
                        continue;
                    }
                    // Advance the cadence deadline instead of re-anchoring to
                    // `now`: anchoring drops every slightly-early frame, which
                    // beats a ~24fps camera paced at 24 down to ~half rate.
                    // Re-anchor only after a gap so slow input earns no credit.
                    nextDueMs = nextDueMs === null || nowMs - nextDueMs > intervalMs
                        ? nowMs + intervalMs
                        : nextDueMs + intervalMs;
                }
                else {
                    nextDueMs = null;
                }
                // Survivors feed the encode-deficit denominator (see RecorderStats).
                captured.stats.framesOffered++;
                yield captured;
            }
        }
    };
}

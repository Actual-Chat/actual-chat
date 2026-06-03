// Demand-driven temporal downsampler. The recorder mutates `targetFps` from
// the aggregate receiver demand (MaxRequestedLayerId → fps): non-focused
// streams pace to ~10fps, the focused stream runs full rate. `targetFps <= 0`
// drops every frame — the idle case where no viewer wants the stream, so we
// stop feeding the encoders (CPU ~0) while the camera track stays warm for
// instant resume. A non-finite target (the default) is a no-op: every frame
// passes. Like floodGate, it sits right after capture so paced-out frames
// release their GPU frame without traversing downscale/encode.

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
            let lastEmittedMs: number | null = null;
            for await (const captured of source) {
                const fps = state.targetFps;
                if (fps <= 0) {
                    try { captured.frame.close(); } catch { /* ignore */ }
                    continue;
                }
                const nowMs = performance.now();
                if (lastEmittedMs !== null && Number.isFinite(fps)) {
                    const minIntervalMs = 1000 / fps;
                    if (nowMs - lastEmittedMs < minIntervalMs - JITTER_EPS_MS) {
                        try { captured.frame.close(); } catch { /* ignore */ }
                        continue;
                    }
                }
                lastEmittedMs = nowMs;
                // Survivors feed the encode-deficit denominator (see RecorderStats).
                captured.stats.framesOffered++;
                yield captured;
            }
        }
    };
}

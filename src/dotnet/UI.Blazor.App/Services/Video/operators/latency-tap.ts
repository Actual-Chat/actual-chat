import { getLogs } from 'logging';
import { tap, type PipeOperator } from 'ix-ext';
import type { DecodedFrame } from '../frame-envelopes';

const { debugLog } = getLogs('VideoPipeline');

export interface LatencySample {
    /** `now() − decodedAt.timeMs` — receiver-domain. */
    frameAgeMs: number;
    /** `now() − capturedAt.timeMs` — cross-clock approximation. The
     *  sender / receiver `MonotonicClock`s share a Unix-epoch anchor
     *  but drift independently — soft KPI, not an SLO. */
    e2eLatencyMs: number;
    /** Sender's `capturedAt.epoch` — consumers may want to drop the
     *  first sample after a flip (e2e jumps on resync). */
    capturedEpoch: number;
    layerId: number;
    /** Cumulative `pullSource` bytes received this run. VideoQualityUI
     *  derives bitrate from this; 0 here pegs the cap at L0. */
    bytesReceived: number;
    /** Filled in by `Player.start`'s wrapped `report` (the operator has
     *  no buffer reference). VideoQualityUI's classifier uses this. */
    bufferSpanMs: number;
}

export interface LatencyTapOptions {
    /** Default 1000 ms. Tighter cadences hurt RPC throughput for no benefit. */
    intervalMs?: number;
    report: (sample: LatencySample) => void;
    /** MUST share clock domain with `decode.ts`'s `decodedAt`
     *  (`performance.now`). Using `Date.now` would cross-subtract Unix
     *  millis from a monotonic time and trip skip-to-live every sample. */
    now?: () => number;
}

const DEFAULT_INTERVAL_MS = 1000;

// Cadence is driven by frame arrival, not `setInterval`, so a stalled
// stream produces no spurious reports. First frame emits immediately.
export function latencyTap(opts: LatencyTapOptions): PipeOperator<DecodedFrame, DecodedFrame> {
    const intervalMs = opts.intervalMs ?? DEFAULT_INTERVAL_MS;
    const now = opts.now ?? ((): number => performance.now());
    const { report } = opts;
    let lastReportAtMs = Number.NEGATIVE_INFINITY;
    return tap((envelope: DecodedFrame): void => {
        try {
            const nowMs = now();
            if (nowMs - lastReportAtMs < intervalMs) return;

            lastReportAtMs = nowMs;
            report({
                frameAgeMs: nowMs - envelope.decodedAt.timeMs,
                e2eLatencyMs: nowMs - envelope.capturedAt.timeMs,
                capturedEpoch: envelope.capturedAt.epoch,
                layerId: envelope.layerId,
                bytesReceived: envelope.stats.bytesReceived,
                bufferSpanMs: 0,
            });
        } catch (e) {
            debugLog?.log('latencyTap: sample failed:', e);
        }
    });
}

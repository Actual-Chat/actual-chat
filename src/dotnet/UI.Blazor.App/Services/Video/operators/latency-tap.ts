import { getLogs } from 'logging';
import { tap, type PipeOperator } from 'ix-ext';
import type { DecodedFrame } from '../frame-envelopes';

const { debugLog } = getLogs('VideoPipeline');

export interface LatencySample {
    frameAgeMs: number;
    // Cross-clock approximation: sender/receiver MonotonicClocks share a Unix anchor
    // but drift independently — soft KPI, not an SLO.
    e2eLatencyMs: number;
    capturedEpoch: number;
    layerId: number;
    bytesReceived: number;
    // Filled in by Player.start's wrapped report (the operator has no buffer reference).
    bufferSpanMs: number;
}

export interface LatencyTapOptions {
    intervalMs?: number;
    report: (sample: LatencySample) => void;
    // MUST share clock domain with decode.ts's decodedAt (performance.now).
    // Date.now would cross-subtract Unix millis from monotonic and trip skip-to-live every sample.
    now?: () => number;
}

const DEFAULT_INTERVAL_MS = 1000;

// Cadence is driven by frame arrival, not setInterval, so a stalled stream
// produces no spurious reports. First frame emits immediately.
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

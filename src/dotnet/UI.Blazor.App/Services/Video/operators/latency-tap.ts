import { getLogs } from 'logging';
import { tap, type PipeOperator } from 'ix-ext';
import type { DecodedFrame, PlayerStats } from '../frame-envelopes';
import type { RotationQuarter } from 'orientation';

const { debugLog } = getLogs('VideoPipeline');

export interface LatencySample {
    frameAgeMs: number;
    // Cross-clock approximation: sender/receiver MonotonicClocks share a Unix anchor
    // but drift independently — soft KPI, not an SLO.
    e2eLatencyMs: number;
    // Presented frame's offset from stream start (ms), reconstructed on the
    // receiver from wire Offset. Same units as audio's sourceOffsetMs — the
    // A/V-sync lag is startedAtMs + capturedAtMs vs ServerClock.now.
    capturedAtMs: number;
    capturedEpoch: number;
    // Server-stamped receive time (Unix ms); 0 when absent. With capturedAtMs +
    // startedAtMs (server domain) this isolates uplink (capture→server).
    serverArrivedAtUnixMs: number;
    layerId: number;
    width: number;
    height: number;
    // Quarter-turn CW the receiver should apply to display upright.
    // An out-of-cadence sample fires whenever this value changes, so the
    // presenter can swap orientation without waiting for the 1 Hz interval.
    rotation: RotationQuarter;
    bytesReceived: number;
    // Filled in by Player.start's wrapped report (the operator has no buffer reference).
    bufferSpanMs: number;
    // Live reference — the consumer sees up-to-date counters (presented,
    // dropTrace) at the moment it reads, not a stale copy.
    playerStats: PlayerStats;
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
// Additionally fires an out-of-cadence sample whenever rotation changes, so
// the receiver can swap presentation transform without waiting up to 1 s.
export function latencyTap(opts: LatencyTapOptions): PipeOperator<DecodedFrame, DecodedFrame> {
    const intervalMs = opts.intervalMs ?? DEFAULT_INTERVAL_MS;
    const now = opts.now ?? ((): number => performance.now());
    const { report } = opts;
    let lastReportAtMs = Number.NEGATIVE_INFINITY;
    let lastReportedRotation: RotationQuarter = -1;
    return tap((envelope: DecodedFrame): void => {
        try {
            const nowMs = now();
            const rotationChanged = envelope.rotation !== lastReportedRotation;
            if (!rotationChanged && nowMs - lastReportAtMs < intervalMs) return;

            lastReportAtMs = nowMs;
            lastReportedRotation = envelope.rotation;
            report({
                frameAgeMs: nowMs - envelope.decodedAt.timeMs,
                e2eLatencyMs: nowMs - envelope.capturedAt.timeMs,
                capturedAtMs: envelope.capturedAt.timeMs,
                capturedEpoch: envelope.capturedAt.epoch,
                serverArrivedAtUnixMs: envelope.serverArrivedAtUnixMs,
                layerId: envelope.layerId,
                width: envelope.frame.displayWidth,
                height: envelope.frame.displayHeight,
                rotation: envelope.rotation,
                bytesReceived: envelope.stats.bytesReceived,
                bufferSpanMs: 0,
                playerStats: envelope.stats,
            });
        } catch (e) {
            debugLog?.log('latencyTap: sample failed:', e);
        }
    });
}

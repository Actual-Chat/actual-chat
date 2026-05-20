import { tap, type PipeOperator } from 'ix-ext';
import { RunningEMA } from 'math';
import type { ArrivedChunk, PlayerStats } from '../frame-envelopes';

// Above-floor downlink-latency tracker. The receiver has no clock sync with
// the server, so we subtract a sliding-60s minimum of `rawLatency` to isolate
// the variable above-floor component. NTP steps reset the floor within one
// window; constant skew cancels out exactly.
const EMA_ALPHA = 0.2;
const BASELINE_WINDOW_MS = 60_000;

export interface DownlinkTapOptions {
    stats: PlayerStats;
    // Injectable wallclock for tests. Production uses Date.now().
    now?: () => number;
}

export function downlinkTap(opts: DownlinkTapOptions): PipeOperator<ArrivedChunk, ArrivedChunk> {
    const { stats } = opts;
    const now = opts.now ?? ((): number => Date.now());
    const latencyEma = new RunningEMA(0, 1, EMA_ALPHA);
    const intervalEma = new RunningEMA(0, 1, EMA_ALPHA);
    // Monotonic deque of [wallMs, rawLatencyMs] kept sorted ascending by
    // rawLatencyMs so the front is always the sliding-window minimum.
    const deque: [number, number][] = [];
    let lastArrivedWallMs = -1;
    return tap((chunk: ArrivedChunk): void => {
        const wallNowMs = now();

        if (lastArrivedWallMs >= 0) {
            intervalEma.appendSample(wallNowMs - lastArrivedWallMs);
            stats.arrivalIntervalEma = intervalEma.value;
        }
        lastArrivedWallMs = wallNowMs;

        if (chunk.serverArrivedAtUnixMs <= 0)
            return;

        const rawLatencyMs = wallNowMs - chunk.serverArrivedAtUnixMs;
        while (deque.length > 0 && deque[deque.length - 1][1] >= rawLatencyMs)
            deque.pop();
        deque.push([wallNowMs, rawLatencyMs]);
        while (deque.length > 0 && wallNowMs - deque[0][0] > BASELINE_WINDOW_MS)
            deque.shift();

        const minBaselineMs = deque[0][1];
        stats.downlinkMinBaselineMs = minBaselineMs;
        latencyEma.appendSample(Math.max(0, rawLatencyMs - minBaselineMs));
        stats.downlinkLatencyEma = latencyEma.value;
    });
}

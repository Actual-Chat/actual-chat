import { describe, it, expect } from 'vitest';
import { downlinkTap } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downlink-tap';
import {
    createEmptyPlayerStats,
    type ArrivedChunk,
    type PlayerStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

function envelope(serverArrivedAtUnixMs: number, stats: PlayerStats): ArrivedChunk {
    return {
        chunk: {} as EncodedVideoChunk,
        arrivedAt: { timeMs: 0, epoch: 0 },
        capturedAt: { timeMs: 0, epoch: 0 },
        index: 0,
        dropTrace: [],
        serverArrivedAtUnixMs,
        isKeyFrame: false,
        layerId: 0,
        width: 0,
        height: 0,
        rawByteLength: 0,
        rotation: 0,
        stats,
    };
}

function source(items: ArrivedChunk[]): AsyncIterable<ArrivedChunk> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

describe('downlinkTap', () => {
    it('stable wallclock skew yields zero downlink latency floor', async () => {
        // Server clock 5000 ms ahead of receiver. The receiver's rawLatency
        // is a constant -5000 ms; the sliding-min baseline equals -5000 ms;
        // downlink latency = raw - baseline = 0.
        const stats = createEmptyPlayerStats();
        const chunks: ArrivedChunk[] = [];
        const times: number[] = [];
        for (let i = 0; i < 10; i++) {
            chunks.push(envelope(100_000 + i * 33 + 5_000, stats));
            times.push(100_000 + i * 33);
        }
        let tickIdx = 0;
        const tap = downlinkTap({ stats, now: () => times[tickIdx++] });

        await drain(tap(source(chunks)));

        expect(stats.downlinkMinBaselineMs).toBe(-5_000);
        expect(stats.downlinkLatencyEma).toBe(0);
    });

    it('step in latency raises EMA above zero', async () => {
        // First 5 frames at floor (raw = 0); then 15 frames with +400 ms
        // injected as above-floor delay. Baseline stays at 0, EMA rises.
        const stats = createEmptyPlayerStats();
        const chunks: ArrivedChunk[] = [];
        const times: number[] = [];
        let receiverNow = 200_000;
        let serverArrived = 200_000;
        for (let i = 0; i < 5; i++) {
            chunks.push(envelope(serverArrived, stats));
            times.push(receiverNow);
            receiverNow += 33;
            serverArrived += 33;
        }
        for (let i = 0; i < 15; i++) {
            chunks.push(envelope(serverArrived, stats));
            times.push(receiverNow + 400);
            receiverNow += 33;
            serverArrived += 33;
        }
        let tickIdx = 0;
        const tap = downlinkTap({ stats, now: () => times[tickIdx++] });

        await drain(tap(source(chunks)));

        expect(stats.downlinkMinBaselineMs).toBe(0);
        expect(stats.downlinkLatencyEma).toBeGreaterThan(100);
        expect(stats.downlinkLatencyEma).toBeLessThanOrEqual(400);
    });

    it('legacy frames (serverArrivedAtUnixMs = 0) leave latency unset', async () => {
        const stats = createEmptyPlayerStats();
        const chunks = [
            envelope(0, stats),
            envelope(0, stats),
            envelope(0, stats),
        ];
        const times = [100_000, 100_033, 100_066];
        let tickIdx = 0;
        const tap = downlinkTap({ stats, now: () => times[tickIdx++] });

        await drain(tap(source(chunks)));

        expect(stats.downlinkLatencyEma).toBe(-1);
        expect(stats.downlinkMinBaselineMs).toBe(-1);
    });

    it('arrival interval EMA tracks wallclock delta between chunks', async () => {
        const stats = createEmptyPlayerStats();
        const chunks = [
            envelope(0, stats),
            envelope(0, stats),
            envelope(0, stats),
            envelope(0, stats),
        ];
        const times = [100_000, 100_033, 100_066, 100_099];
        let tickIdx = 0;
        const tap = downlinkTap({ stats, now: () => times[tickIdx++] });

        await drain(tap(source(chunks)));

        // First chunk doesn't produce an interval; rest are 33 ms apart.
        expect(stats.arrivalIntervalEma).toBe(33);
    });
});

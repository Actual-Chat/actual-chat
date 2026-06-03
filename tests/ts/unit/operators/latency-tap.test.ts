import { describe, it, expect } from 'vitest';
import {
    latencyTap,
    type LatencySample,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/latency-tap';
import {
    createEmptyPlayerStats,
    type DecodedFrame,
    type PlayerStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
// ---- Mocks ----------------------------------------------------------------

class MockVideoFrame {
    closed = false;
    constructor(public id = 0) {}
    close(): void { this.closed = true; }
    codedWidth = 320;
    codedHeight = 180;
    displayWidth = 320;
    displayHeight = 180;
}

// ---- Helpers --------------------------------------------------------------

interface MakeOpts {
    capturedTimeMs: number;
    capturedEpoch?: number;
    arrivedTimeMs?: number;
    decodedTimeMs: number;
    layerId?: number;
    serverArrivedAtUnixMs?: number;
}

function makeEnvelope(stats: PlayerStats, id: number, opts: MakeOpts): DecodedFrame {
    return {
        frame: new MockVideoFrame(id) as unknown as VideoFrame,
        capturedAt: { timeMs: opts.capturedTimeMs, epoch: opts.capturedEpoch ?? 0 },
        arrivedAt: { timeMs: opts.arrivedTimeMs ?? opts.capturedTimeMs + 5, epoch: opts.capturedEpoch ?? 0 },
        serverArrivedAtUnixMs: opts.serverArrivedAtUnixMs ?? 0,
        decodedAt: { timeMs: opts.decodedTimeMs, epoch: opts.capturedEpoch ?? 0 },
        index: id,
        dropTrace: [],
        layerId: opts.layerId ?? 0,
        rotation: 0,
        stats,
    };
}

function source(items: DecodedFrame[]): AsyncIterable<DecodedFrame> {
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

// ---- Tests ----------------------------------------------------------------

describe('latencyTap', () => {
    it('frames pass through unchanged (passthrough operator)', async () => {
        const stats = createEmptyPlayerStats();
        const samples: LatencySample[] = [];
        const items = Array.from({ length: 5 }, (_, i) => makeEnvelope(stats, i, {
            capturedTimeMs: 1_000 + i * 33,
            decodedTimeMs: 1_050 + i * 33,
        }));

        const op = latencyTap({
            intervalMs: 1_000,
            now: () => 2_000,
            report: s => samples.push(s),
        });
        const out = await drain(op(source(items)));

        expect(out).toHaveLength(5);
        for (let i = 0; i < 5; i++) {
            expect(out[i]).toBe(items[i]);
        }
    });

    it('first frame produces an immediate sample; subsequent samples are throttled to intervalMs', async () => {
        const stats = createEmptyPlayerStats();
        const samples: LatencySample[] = [];
        // 5 frames at 33 ms intervals on the receiver clock, intervalMs=100 →
        // expect samples on frame 0, then on the first frame whose now()
        // crosses the +100 ms boundary, then again at +200 ms.
        const nowValues = [1000, 1033, 1066, 1100, 1133];
        let nowIdx = 0;
        const items = nowValues.map((t, i) => makeEnvelope(stats, i, {
            capturedTimeMs: t - 50,
            decodedTimeMs: t - 5,
        }));

        const op = latencyTap({
            intervalMs: 100,
            now: () => nowValues[nowIdx++],
            report: s => samples.push(s),
        });
        await drain(op(source(items)));

        // Expected sample frames: idx 0 (immediate), idx 3 (1100 ≥ 1000 + 100),
        // and no fourth (idx 4 at 1133 < 1100 + 100). Two samples total.
        expect(samples).toHaveLength(2);
    });

    it('sample fields are correct: frameAgeMs, e2eLatencyMs, capturedEpoch, layerId', async () => {
        const stats = createEmptyPlayerStats();
        const samples: LatencySample[] = [];
        const items = [
            makeEnvelope(stats, 0, {
                capturedTimeMs: 1_000,
                decodedTimeMs: 1_080,
                capturedEpoch: 7,
                layerId: 2,
            }),
        ];

        const op = latencyTap({
            intervalMs: 1_000,
            now: () => 1_100,
            report: s => samples.push(s),
        });
        await drain(op(source(items)));

        expect(samples).toHaveLength(1);
        expect(samples[0].frameAgeMs).toBe(20);     // 1100 − 1080
        expect(samples[0].e2eLatencyMs).toBe(100);  // 1100 − 1000
        expect(samples[0].capturedEpoch).toBe(7);
        expect(samples[0].layerId).toBe(2);
    });

    it('default intervalMs is 1000 ms; default now is Date.now', async () => {
        const stats = createEmptyPlayerStats();
        const samples: LatencySample[] = [];
        // Two frames; pin Date.now() so the first is sampled and the second
        // (sub-1000 ms later) is throttled out.
        const realDateNow = Date.now;
        const t0 = 50_000;
        let i = 0;
        Date.now = () => t0 + i++ * 100;
        try {
            const items = [
                makeEnvelope(stats, 0, { capturedTimeMs: t0 - 30, decodedTimeMs: t0 - 10 }),
                makeEnvelope(stats, 1, { capturedTimeMs: t0 + 70, decodedTimeMs: t0 + 90 }),
            ];
            const op = latencyTap({ report: s => samples.push(s) });
            await drain(op(source(items)));
        } finally {
            Date.now = realDateNow;
        }
        // Only the first frame produced a sample (default 1000 ms throttle).
        expect(samples).toHaveLength(1);
    });
});

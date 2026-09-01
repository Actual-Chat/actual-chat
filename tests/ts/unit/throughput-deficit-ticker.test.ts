import { describe, it, expect } from 'vitest';
import { EncodeDeficitTicker, ThroughputDeficitTicker } from
    '../../../src/dotnet/UI.Blazor.App/Services/Video/throughput-deficit-ticker';

describe('ThroughputDeficitTicker', () => {
    it('reports 0 when output matches input', () => {
        const t = new ThroughputDeficitTicker(0.3);
        t.tick(10, 10);
        expect(t.value).toBe(0);
    });

    it('reports proportional deficit when codec lags behind input', () => {
        const t = new ThroughputDeficitTicker(0.3);
        // 5 of 10 outputs missed → deficit 0.5, EMA after 1 sample = 0.3 * 0.5 = 0.15.
        t.tick(5, 10);
        expect(t.value).toBeCloseTo(0.15, 6);
    });

    it('clamps to [0, 1] when output exceeds input', () => {
        const t = new ThroughputDeficitTicker(0.3);
        t.tick(20, 10);
        expect(t.value).toBe(0);
    });

    it('skips ticks with zero input', () => {
        const t = new ThroughputDeficitTicker(0.3);
        t.tick(5, 10);
        const before = t.value;
        t.tick(0, 0);
        expect(t.value).toBe(before);
    });

    it('converges towards sustained deficit over many ticks', () => {
        const t = new ThroughputDeficitTicker(0.3);
        for (let i = 0; i < 50; i++)
            t.tick(8, 10); // sustained deficit 0.2
        expect(t.value).toBeCloseTo(0.2, 3);
    });

    it('converges back to 0 when codec recovers', () => {
        const t = new ThroughputDeficitTicker(0.3);
        for (let i = 0; i < 10; i++)
            t.tick(5, 10);
        expect(t.value).toBeGreaterThan(0.3);
        for (let i = 0; i < 50; i++)
            t.tick(10, 10);
        expect(t.value).toBeLessThan(0.01);
    });

    it('reset returns value to 0', () => {
        const t = new ThroughputDeficitTicker(0.3);
        t.tick(0, 10);
        expect(t.value).toBeGreaterThan(0);
        t.reset();
        expect(t.value).toBe(0);
    });
});

// Mirrors the decode-deficit computation in VideoPlayer.onLatencyReport: the
// per-tick input is effectiveArrived = chunksReceived - decoderQueueSize, so
// frames merely in flight (not lost) don't register as a deficit. Proves the
// "BAD-at-queue-0" artifact (bursty arrival inflating the raw ratio, then the
// EMA freezing while the queue is empty) is gone with the subtraction.
describe('decode-deficit with in-flight subtraction', () => {
    interface Sample { chunksReceived: number; framesDecoded: number; decoderQueueSize: number }

    function runEffective(samples: Sample[]): number {
        const t = new ThroughputDeficitTicker(0.3);
        let lastFrames = 0;
        let lastEffective = 0;
        for (const s of samples) {
            const framesDelta = Math.max(0, s.framesDecoded - lastFrames);
            const effective = s.chunksReceived - s.decoderQueueSize;
            const effectiveDelta = Math.max(0, effective - lastEffective);
            t.tick(framesDelta, effectiveDelta);
            lastFrames = s.framesDecoded;
            lastEffective = effective;
        }
        return t.value;
    }

    function runRaw(samples: Sample[]): number {
        const t = new ThroughputDeficitTicker(0.3);
        let lastFrames = 0;
        let lastChunks = 0;
        for (const s of samples) {
            const framesDelta = Math.max(0, s.framesDecoded - lastFrames);
            const chunksDelta = Math.max(0, s.chunksReceived - lastChunks);
            t.tick(framesDelta, chunksDelta);
            lastFrames = s.framesDecoded;
            lastChunks = s.chunksReceived;
        }
        return t.value;
    }

    // Bursty arrival then drain to an empty decoder queue, with zero real loss.
    const burstThenDrain: Sample[] = [
        { chunksReceived: 30, framesDecoded: 20, decoderQueueSize: 10 }, // burst: 10 in flight
        { chunksReceived: 30, framesDecoded: 30, decoderQueueSize: 0 },  // drain to queue 0
        { chunksReceived: 30, framesDecoded: 30, decoderQueueSize: 0 },  // quiet at queue 0
    ];

    it('stays ~0 across a burst-then-drain with no real loss', () => {
        expect(runEffective(burstThenDrain)).toBeLessThan(0.03);
    });

    it('the raw (no-subtraction) computation would misfire on the same sequence', () => {
        expect(runRaw(burstThenDrain)).toBeGreaterThan(0.03);
    });

    it('still registers genuine loss (frames lost, not just in flight)', () => {
        // 10 chunks arrive, queue drains to 0, but only 8 decoded → 2 truly lost.
        const lossy: Sample[] = [];
        for (let i = 1; i <= 40; i++)
            lossy.push({ chunksReceived: i * 10, framesDecoded: i * 8, decoderQueueSize: 0 });
        expect(runEffective(lossy)).toBeGreaterThan(0.1);
    });
});

describe('EncodeDeficitTicker', () => {
    // The reported symptom: the health monitor starts when the worker is
    // asked to start, so the opening ticks span camera attach and encoder
    // configuration. They scored a full deficit and latched the sender-health
    // classifier Bad (α=0.3 reaches ~0.51 in two ticks, past the 0.15
    // threshold) before the encoder had done anything wrong.
    it('stays at 0 while the encoder is still producing its first bundle', () => {
        const t = new EncodeDeficitTicker(0.3);

        t.tick(0, 0, 30);
        t.tick(0, 0, 30);

        expect(t.value).toBe(0);
    });

    it('does not charge the window that spans the startup gap', () => {
        const t = new EncodeDeficitTicker(0.3);

        t.tick(0, 0, 30);
        t.tick(5, 5, 30); // first output: 5 of 30, but 25 were pre-first-chunk

        expect(t.value).toBe(0);
    });

    it('measures normally once the encoder is warm', () => {
        const t = new EncodeDeficitTicker(0.3);
        t.tick(0, 0, 30);
        t.tick(5, 5, 30);

        t.tick(35, 30, 30);
        expect(t.value).toBe(0);

        t.tick(50, 15, 30); // half the offered frames
        expect(t.value).toBeCloseTo(0.15, 5);
    });

    // The health monitor starts when the worker is asked to start, not when
    // frames begin flowing, so no-input ticks come first on a slow camera
    // attach. Spending grace on them exhausted it before the encoder was ever
    // handed a frame, which resurrected the very bug this class exists to fix.
    it('does not spend the grace period on ticks with no frames offered', () => {
        const t = new EncodeDeficitTicker(0.3, 3);

        for (let i = 0; i < 10; i++) t.tick(0, 0, 0);  // camera not up yet
        t.tick(0, 0, 30);                              // frames flow, encoder still silent
        t.tick(0, 0, 30);

        expect(t.value).toBe(0);
    });

    // The grace period is bounded, or a dead encoder would read as perfect.
    it('reports a full deficit for an encoder that never produces anything', () => {
        const t = new EncodeDeficitTicker(0.3, 3);

        for (let i = 0; i < 8; i++) t.tick(0, 0, 30);

        expect(t.value).toBeGreaterThan(0.15);
    });

    it('resets its startup state', () => {
        const t = new EncodeDeficitTicker(0.3, 3);
        for (let i = 0; i < 8; i++) t.tick(0, 0, 30);
        expect(t.value).toBeGreaterThan(0.15);

        t.reset();
        t.tick(0, 0, 30);

        expect(t.value).toBe(0);
    });
});

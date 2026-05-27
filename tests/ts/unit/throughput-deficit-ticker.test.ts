import { describe, it, expect } from 'vitest';
import { ThroughputDeficitTicker } from
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

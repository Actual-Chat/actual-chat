import { describe, it, expect } from 'vitest';
import { BufferSpanMeter } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/buffer-span-meter';

describe('BufferSpanMeter', () => {
    it('first sample blends to itself (both windows see the lone sample)', () => {
        const m = new BufferSpanMeter();
        expect(m.sample(1000, 0)).toBe(1000);
    });

    it('rejects a transient HIGH spike (windowed min keeps the low floor)', () => {
        const m = new BufferSpanMeter();
        m.sample(100, 0);
        m.sample(100, 100);
        // One-frame spike to 9000: min over both windows still includes the 100s.
        expect(m.sample(9000, 200)).toBe(100);
    });

    it('tracks a sustained-deep buffer once the low samples age out', () => {
        const m = new BufferSpanMeter({ shortWindowMs: 300, longWindowMs: 1500 });
        m.sample(100, 0);
        // Feed 5000 until both windows have fully aged past the initial 100.
        m.sample(5000, 500);
        m.sample(5000, 1000);
        m.sample(5000, 1600);   // 100@0 now older than longWindow (1500) → evicted
        expect(m.sample(5000, 2000)).toBe(5000);
    });

    it('windows evict independently (short drops the floor before long)', () => {
        const m = new BufferSpanMeter({ shortWindowMs: 300, longWindowMs: 1500 });
        m.sample(100, 0);
        // At t=400 the 100@0 is older than the 300ms short window (evicted) but
        // still inside the 1500ms long window. shortMin=5000, longMin=100.
        expect(m.sample(5000, 400)).toBeCloseTo(0.6 * 5000 + 0.4 * 100, 6);
    });

    it('honors custom weights', () => {
        const m = new BufferSpanMeter({ shortWindowMs: 300, longWindowMs: 1500, shortWeight: 0.5, longWeight: 0.5 });
        m.sample(100, 0);
        expect(m.sample(5000, 400)).toBeCloseTo(0.5 * 5000 + 0.5 * 100, 6);
    });

    it('reset clears both windows', () => {
        const m = new BufferSpanMeter({ shortWindowMs: 300, longWindowMs: 1500 });
        m.sample(100, 0);
        m.sample(100, 100);
        m.reset();
        // After reset the next sample is a fresh lone value, not blended with 100.
        expect(m.sample(5000, 200)).toBe(5000);
    });
});

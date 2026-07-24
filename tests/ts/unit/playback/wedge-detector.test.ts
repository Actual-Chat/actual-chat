import { describe, it, expect } from 'vitest';
import { WedgeDetector } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/wedge-detector';
import { createEmptyPlayerStats } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

function statsAt(over: Partial<ReturnType<typeof createEmptyPlayerStats>>) {
    return { ...createEmptyPlayerStats(), ...over };
}

describe('WedgeDetector', () => {
    it('reports present-wedge when bytes+decoded advance but presented is frozen', () => {
        const d = new WedgeDetector(6_000);
        expect(d.onSample(statsAt({ bytesReceived: 100, framesDecoded: 10, presented: 5 }), 0)).toBeNull();
        expect(d.onSample(statsAt({ bytesReceived: 200, framesDecoded: 20, presented: 5 }), 3_000)).toBeNull();
        const diag = d.onSample(statsAt({
            bytesReceived: 300, framesDecoded: 30, presented: 5,
            presentState: 'mstg:awaiting-ready', feedPumpState: 'blocked',
        }), 7_000);
        expect(diag?.kind).toBe('present-wedge');
        expect(diag?.frozenMs).toBeGreaterThanOrEqual(6_000);
        expect(diag?.detail).toContain('mstg:awaiting-ready');
    });

    it('reports decode-wedge when bytes advance but decoded and presented are frozen', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, framesDecoded: 10, presented: 5 }), 0);
        const diag = d.onSample(statsAt({ bytesReceived: 900, framesDecoded: 10, presented: 5 }), 7_000);
        expect(diag?.kind).toBe('decode-wedge');
    });

    it('stays silent while presented advances', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 0);
        expect(d.onSample(statsAt({ bytesReceived: 200, presented: 6 }), 7_000)).toBeNull();
        expect(d.hasProgress).toBe(true);
    });

    it('stays silent when the source is starved (bytes frozen too)', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 0);
        expect(d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 20_000)).toBeNull();
    });

    it('progress resets the freeze window', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 0);
        d.onSample(statsAt({ bytesReceived: 200, presented: 6 }), 5_000);
        expect(d.onSample(statsAt({ bytesReceived: 300, presented: 6 }), 9_000)).toBeNull();
    });

    it('hasProgress is false on first sample and after reset', () => {
        const d = new WedgeDetector(6_000);
        expect(d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 0)).toBeNull();
        expect(d.hasProgress).toBe(false);
        d.reset();
        expect(d.onSample(statsAt({ bytesReceived: 200, presented: 10 }), 20_000)).toBeNull();
        expect(d.hasProgress).toBe(false);
    });

    it('byte-starved samples do not age the freeze window', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 0);
        // flowing + frozen for 4s
        expect(d.onSample(statsAt({ bytesReceived: 200, presented: 5 }), 4_000)).toBeNull();
        // starved (bytes frozen too) for 20s — must not count toward the window
        expect(d.onSample(statsAt({ bytesReceived: 200, presented: 5 }), 24_000)).toBeNull();
        // flowing again for 1s — cumulative flowing-frozen time is now 5s
        expect(d.onSample(statsAt({ bytesReceived: 300, presented: 5 }), 25_000)).toBeNull();
        // one more flowing second pushes cumulative flowing-frozen time to 6s
        const diag = d.onSample(statsAt({ bytesReceived: 400, presented: 5 }), 26_000);
        expect(diag).not.toBeNull();
        expect(diag?.frozenMs).toBeGreaterThanOrEqual(6_000);
    });

    it('fresh byte flow after reset needs the full window', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 0);
        d.onSample(statsAt({ bytesReceived: 200, presented: 6 }), 3_000);
        d.reset();
        expect(d.onSample(statsAt({ bytesReceived: 300, presented: 6 }), 20_000)).toBeNull();
        expect(d.onSample(statsAt({ bytesReceived: 400, presented: 6 }), 25_000)).toBeNull();
    });
});

import { describe, it, expect } from 'vitest';
import { pickSource } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downscale';
import type { LayerSpec } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downscale';

describe('pickSource', () => {
    const ladder = (...specs: [number, number][]): LayerSpec[] =>
        specs.map(([w, h]) => ({ width: w, height: h }));

    it('top layer always uses the original input', () => {
        const l = ladder([320, 180], [640, 360], [1280, 720]);
        // No higher tiers painted yet.
        expect(pickSource(2, l, [])).toEqual({ kind: 'original' });
        // Even with bookkeeping (shouldn't matter for top tier).
        expect(pickSource(2, l, [2])).toEqual({ kind: 'original' });
    });

    it('lower tier uses original when no higher tier qualifies (≥ 2× rule)', () => {
        // L0=480, L1=720 → 720/480 = 1.5, doesn't qualify.
        const l = ladder([480, 270], [720, 405]);
        expect(pickSource(0, l, [1])).toEqual({ kind: 'original' });
    });

    it('lower tier uses higher tier when long-edge ≥ 2×', () => {
        // L0=320, L1=480, L2=720. For L0:
        //   L1 (480 / 320 = 1.5) → no
        //   L2 (720 / 320 = 2.25) → yes
        const l = ladder([320, 180], [480, 270], [720, 405]);
        expect(pickSource(0, l, [1, 2])).toEqual({ kind: 'higher', idx: 2 });
    });

    it('picks the smallest qualifying higher tier (least drawImage work)', () => {
        // L0=320, L1=640, L2=1280. For L0:
        //   L1 (640/320=2) qualifies; L2 (1280/320=4) qualifies.
        //   Pick smallest: L1.
        const l = ladder([320, 180], [640, 360], [1280, 720]);
        expect(pickSource(0, l, [1, 2])).toEqual({ kind: 'higher', idx: 1 });
    });

    it('uses original when only painted higher tiers all fail the threshold', () => {
        // L0=480, L1=720, L2=900. For L0: 720/480=1.5, 900/480=1.875. Neither.
        const l = ladder([480, 270], [720, 405], [900, 506]);
        expect(pickSource(0, l, [1, 2])).toEqual({ kind: 'original' });
    });

    it('threshold is exact (≥ 2× passes; < 2× fails)', () => {
        // L0=400, L1=800 → 800/400 = 2 exactly, qualifies.
        const l1 = ladder([400, 225], [800, 450]);
        expect(pickSource(0, l1, [1])).toEqual({ kind: 'higher', idx: 1 });

        // L0=401, L1=800 → 800/401 ≈ 1.995, fails.
        const l2 = ladder([401, 226], [800, 450]);
        expect(pickSource(0, l2, [1])).toEqual({ kind: 'original' });
    });

    it('only considers tiers actually painted (paintedHigher set)', () => {
        // L0=320, L1=640, L2=1280; if L1 isn't painted yet, L2 must be the
        // source even though L1 would qualify.
        const l = ladder([320, 180], [640, 360], [1280, 720]);
        expect(pickSource(0, l, [2])).toEqual({ kind: 'higher', idx: 2 });
    });

    it('walkthrough from the design discussion: L0=320, L1=480, L2=720, original=1080', () => {
        const l = ladder([320, 180], [480, 270], [720, 405]);
        // L2 (top): always original.
        expect(pickSource(2, l, [])).toEqual({ kind: 'original' });
        // L1: only higher = L2 (720). 720/480 = 1.5 < 2 → original.
        expect(pickSource(1, l, [2])).toEqual({ kind: 'original' });
        // L0: higher = {L1:480, L2:720}. 480/320=1.5 fail; 720/320=2.25 pass → L2.
        expect(pickSource(0, l, [1, 2])).toEqual({ kind: 'higher', idx: 2 });
    });
});

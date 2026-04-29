import { describe, it, expect } from 'vitest';
import {
    hasHigherTopTier,
    buildLadderForSource,
    MAX_SIMULCAST_TIERS,
    type SpatialLayerConfig,
} from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/simulcast-ladder';

const tier = (width: number, height: number, bitrate = 1_000_000): SpatialLayerConfig =>
    ({ width, height, bitrate });

describe('hasHigherTopTier — ladder persistence', () => {
    it('returns false when incoming top equals existing top', () => {
        const ladder = [tier(640, 360), tier(1280, 720)];
        expect(hasHigherTopTier(ladder, ladder)).toBe(false);
    });

    it('returns true when incoming top is taller than existing top', () => {
        const existing = [tier(320, 180), tier(640, 360), tier(1280, 720)];
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720), tier(1920, 1080)];
        expect(hasHigherTopTier(incoming, existing)).toBe(true);
    });

    it('returns false when incoming top is shorter than existing top', () => {
        // The exact regression: JS-side promoted to 1080p, C# pushes its
        // hardcoded 720p ladder. Should NOT replace.
        const existing = [tier(320, 180), tier(640, 360), tier(1280, 720), tier(1920, 1080)];
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720)];
        expect(hasHigherTopTier(incoming, existing)).toBe(false);
    });

    it('returns false when existing has same height but more layers (length-only difference)', () => {
        // Same top, fewer base tiers. Caller must rely on the length comparison
        // separately; this helper only watches the top.
        const existing = [tier(320, 180), tier(640, 360), tier(1280, 720)];
        const incoming = [tier(640, 360), tier(1280, 720)];
        expect(hasHigherTopTier(incoming, existing)).toBe(false);
    });

    it('returns false on empty ladders', () => {
        expect(hasHigherTopTier([], [tier(1280, 720)])).toBe(false);
        expect(hasHigherTopTier([tier(1280, 720)], [])).toBe(false);
        expect(hasHigherTopTier([], [])).toBe(false);
    });
});

describe('buildLadderForSource — source-shaped ladder', () => {
    // Mimic the hevc bitrate-table row so the bitrate assertion can be exact
    // without pulling the actual table (which transitively imports the logging
    // module — not test-resolvable).
    const hevcBitrate = (height: number): number => {
        if (height >= 2160) return 6_500_000;
        if (height >= 1080) return 3_250_000;
        if (height >= 720) return 2_000_000;
        if (height >= 540) return 1_250_000;
        return 650_000;
    };

    it('landscape 1920x1080 source — top + source/2', () => {
        const result = buildLadderForSource({
            count: 4, srcWidth: 1920, srcHeight: 1080, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '960x540', '1920x1080',
        ]);
    });

    it('portrait 1080x1920 source — portrait tiers, top + source/2', () => {
        // The exact bug from the field logs: with the old hardcoded landscape
        // ladder + augment trick, the wire carried both 1920x1080 (rotated
        // duplicate of source) and 1080x1920 (source). Source-shaped build
        // emits portrait tiers exclusively.
        const result = buildLadderForSource({
            count: 4, srcWidth: 1080, srcHeight: 1920, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '540x960', '1080x1920',
        ]);
    });

    it('landscape 1280x720 source — top + 640x360', () => {
        const result = buildLadderForSource({
            count: 4, srcWidth: 1280, srcHeight: 720, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '640x360', '1280x720',
        ]);
    });

    it('portrait 720x1280 source — portrait orientation, top + source/2', () => {
        const result = buildLadderForSource({
            count: 4, srcWidth: 720, srcHeight: 1280, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '360x640', '720x1280',
        ]);
    });

    it('count cap clamps to MAX_SIMULCAST_TIERS', () => {
        // Server may push count > 2; we clamp internally.
        const result = buildLadderForSource({
            count: 4, srcWidth: 1920, srcHeight: 1080, bitrateFor: hevcBitrate,
        });
        expect(result.length).toBeLessThanOrEqual(MAX_SIMULCAST_TIERS);
        expect(MAX_SIMULCAST_TIERS).toBe(2);
    });

    it('count=2 produces top + source/2', () => {
        const result = buildLadderForSource({
            count: 2, srcWidth: 1920, srcHeight: 1080, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '960x540', '1920x1080',
        ]);
    });

    it('count=1 keeps only the top (source dims)', () => {
        const result = buildLadderForSource({
            count: 1, srcWidth: 1920, srcHeight: 1080, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual(['1920x1080']);
    });

    it('count=0 returns empty', () => {
        const result = buildLadderForSource({
            count: 0, srcWidth: 1920, srcHeight: 1080, bitrateFor: hevcBitrate,
        });
        expect(result).toEqual([]);
    });

    it('zero source dims returns empty (still warming)', () => {
        const result = buildLadderForSource({
            count: 4, srcWidth: 0, srcHeight: 0, bitrateFor: hevcBitrate,
        });
        expect(result).toEqual([]);
    });

    it('540p (960x540) source — top + source/2 (480x270)', () => {
        const result = buildLadderForSource({
            count: 4, srcWidth: 960, srcHeight: 540, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '480x270', '960x540',
        ]);
    });

    it('uses bitrate-table for each tier height', () => {
        const result = buildLadderForSource({
            count: 4, srcWidth: 1920, srcHeight: 1080, bitrateFor: hevcBitrate,
        });
        // Top tier: 1920x1080 → height 1080 → tier 1080 → 3_250_000.
        expect(result[result.length - 1].bitrate).toBe(3_250_000);
        // Bottom (source/2): 960x540 → tier 540 → 1_250_000.
        expect(result[0].bitrate).toBe(1_250_000);
    });

    it('odd source dims: top is source-as-is, bottom even-rounded', () => {
        // Top tier always uses exact source dims (no upscale, encoder already
        // running at those). Bottom tier is source/2 with even-rounding so
        // most encoders accept the dims.
        const result = buildLadderForSource({
            count: 2, srcWidth: 1001, srcHeight: 561, bitrateFor: hevcBitrate,
        });
        expect(result).toHaveLength(2);
        // Top = source (odd dims preserved).
        expect(result[1].width).toBe(1001);
        expect(result[1].height).toBe(561);
        // Bottom = source/2, even-rounded. 1001/2=500.5 → 500. 561/2=280.5 → 280.
        expect(result[0].width % 2).toBe(0);
        expect(result[0].height % 2).toBe(0);
        expect(result[0].width).toBe(500);
        expect(result[0].height).toBe(280);
    });

    it('odd source where source/2 rounds to a power of 2 boundary', () => {
        // 1280x720 → 640x360. Both even. No surprises.
        const result = buildLadderForSource({
            count: 2, srcWidth: 1280, srcHeight: 720, bitrateFor: hevcBitrate,
        });
        expect(result[0].width).toBe(640);
        expect(result[0].height).toBe(360);
    });
});

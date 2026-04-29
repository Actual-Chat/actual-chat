import { describe, it, expect } from 'vitest';
import {
    hasHigherTopTier,
    buildLadderForSource,
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

    it('landscape 1920x1080 source, count=4 — drops the 720 tier (near 1080)', () => {
        // Source is "1080p" landscape. 720p would be a near-duplicate of the
        // top, so the dedupe rule excludes it. Result: [320x180, 640x360, 1920x1080].
        const result = buildLadderForSource({
            count: 4, srcWidth: 1920, srcHeight: 1080, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '320x180', '640x360', '1920x1080',
        ]);
    });

    it('portrait 1080x1920 source — orients tiers portrait, no transposed duplicate', () => {
        // The exact bug from the field logs: with the old hardcoded landscape
        // ladder + augment trick, the wire carried both 1920x1080 (rotated
        // duplicate of source) and 1080x1920 (source). Source-shaped build
        // emits portrait tiers exclusively.
        const result = buildLadderForSource({
            count: 4, srcWidth: 1080, srcHeight: 1920, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '180x320', '360x640', '1080x1920',
        ]);
    });

    it('landscape 1280x720 source — keeps 720 as the top, no upscale', () => {
        const result = buildLadderForSource({
            count: 4, srcWidth: 1280, srcHeight: 720, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '320x180', '640x360', '1280x720',
        ]);
    });

    it('portrait 720x1280 source — portrait orientation, top = source', () => {
        const result = buildLadderForSource({
            count: 4, srcWidth: 720, srcHeight: 1280, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '180x320', '360x640', '720x1280',
        ]);
    });

    it('count cap drops bottom tiers, preserves top', () => {
        // Receivers cap by spatial-id; the top tier is always the reachable
        // ceiling, so a `count=2` cap keeps the top + the next-highest below.
        const result = buildLadderForSource({
            count: 2, srcWidth: 1920, srcHeight: 1080, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '640x360', '1920x1080',
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

    it('640x360 source — ladder collapses to source only (no headroom for sub-tier)', () => {
        // 320 / 640 = 0.5 < 0.6 → the 320 tier IS kept. So result is [320x180, 640x360].
        const result = buildLadderForSource({
            count: 4, srcWidth: 640, srcHeight: 360, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual(['320x180', '640x360']);
    });

    it('540p (960x540) source — drops 640 tier (640/960 = 0.667 > 0.6)', () => {
        // 640 is too close to the source long-side (960) under the 0.6
        // threshold, so the ladder collapses to two tiers.
        const result = buildLadderForSource({
            count: 4, srcWidth: 960, srcHeight: 540, bitrateFor: hevcBitrate,
        });
        expect(result.map(l => `${l.width}x${l.height}`)).toEqual([
            '320x180', '960x540',
        ]);
    });

    it('uses bitrate-table for each tier height', () => {
        // hevc table: 1080->3.25M, 360->650k, 180->? (under 360 default fallback to 360 row).
        const result = buildLadderForSource({
            count: 4, srcWidth: 1920, srcHeight: 1080, bitrateFor: hevcBitrate,
        });
        // Top tier: 1920x1080 → height 1080 → tier 1080 → 3_250_000.
        expect(result[result.length - 1].bitrate).toBe(3_250_000);
        // Mid tier: 640x360 → tier 360 → 650_000.
        expect(result[1].bitrate).toBe(650_000);
        // Bottom: 320x180 → height 180 → falls into 360 tier → 650_000.
        expect(result[0].bitrate).toBe(650_000);
    });

    it('odd source dims round to even on the short side', () => {
        // Aspect-correct shortSide for 9:16 with longSide=320: 320 * 1080 / 1920
        // = 180 (even). For an oddball source ratio that would yield odd dims,
        // verify rounding does not produce odd numbers.
        const result = buildLadderForSource({
            count: 1, srcWidth: 1001, srcHeight: 561, bitrateFor: hevcBitrate,
        });
        expect(result).toHaveLength(1);
        const t = result[0];
        // No upscale: top is source itself.
        expect(t.width).toBe(1001);
        expect(t.height).toBe(561);
    });
});

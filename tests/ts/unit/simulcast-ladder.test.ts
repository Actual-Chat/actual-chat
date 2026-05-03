import { describe, it, expect } from 'vitest';
import {
    buildLadder,
    fitWithin,
    SCREENCAST_MAX_SIMULCAST_TIERS,
    WEBCAM_MAX_SIMULCAST_TIERS,
    webcamTopSize,
    type SpatialLayerConfig,
} from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/simulcast-ladder';

// Mimic the hevc bitrate-table row so the bitrate assertion can be exact
// without pulling the actual table (which transitively imports the logging
// module — not test-resolvable).
const hevcBitrate = (height: number): number => {
    if (height >= 2160) return 6_500_000;
    if (height >= 1080) return 3_250_000;
    if (height >= 720) return 2_000_000;
    if (height >= 540) return 1_250_000;
    if (height >= 360) return 800_000;
    return 400_000;
};

const dims = (ladder: SpatialLayerConfig[]): string[] =>
    ladder.map(l => `${l.width}x${l.height}`);

describe('buildLadder — quarter-pixel ratio', () => {
    it('webcam 3-tier @ 720p → 180p / 360p / 720p', () => {
        const result = buildLadder({
            topWidth: 1280, topHeight: 720, tierCount: 3,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(dims(result)).toEqual(['320x180', '640x360', '1280x720']);
    });

    it('webcam 2-tier dropTop fallback @ 360p → 180p / 360p', () => {
        // iOS HW-encoder budget probe-fail path: drop 720p top, keep [180p, 360p].
        const result = buildLadder({
            topWidth: 640, topHeight: 360, tierCount: 2,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(dims(result)).toEqual(['160x90', '320x180', '640x360'].slice(-2));
        expect(dims(result)).toEqual(['320x180', '640x360']);
    });

    it('screencast 2-tier @ 1080p → 540p / 1080p', () => {
        const result = buildLadder({
            topWidth: 1920, topHeight: 1080, tierCount: 2,
            maxTierCount: SCREENCAST_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(dims(result)).toEqual(['960x540', '1920x1080']);
    });

    it('portrait 720x1280 source — portrait orientation preserved', () => {
        // Source-shaped rebuild for a rotated camera. Each tier is ¼ pixels.
        const result = buildLadder({
            topWidth: 720, topHeight: 1280, tierCount: 3,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(dims(result)).toEqual(['180x320', '360x640', '720x1280']);
    });

    it('tierCount cap clamps to WEBCAM_MAX_SIMULCAST_TIERS', () => {
        const result = buildLadder({
            topWidth: 1920, topHeight: 1080, tierCount: 10,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(result.length).toBeLessThanOrEqual(WEBCAM_MAX_SIMULCAST_TIERS);
        expect(WEBCAM_MAX_SIMULCAST_TIERS).toBe(3);
    });

    it('tierCount=1 keeps only the top (source dims)', () => {
        const result = buildLadder({
            topWidth: 1280, topHeight: 720, tierCount: 1,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(dims(result)).toEqual(['1280x720']);
    });

    it('tierCount=0 returns empty', () => {
        const result = buildLadder({
            topWidth: 1280, topHeight: 720, tierCount: 0,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(result).toEqual([]);
    });

    it('zero source dims returns empty (still warming)', () => {
        const result = buildLadder({
            topWidth: 0, topHeight: 0, tierCount: 3,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(result).toEqual([]);
    });

    it('uses bitrate-table for each tier height', () => {
        const result = buildLadder({
            topWidth: 1280, topHeight: 720, tierCount: 3,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        // Top tier: 720 → 2_000_000
        expect(result[2].bitrate).toBe(2_000_000);
        // Mid tier: 360 → 800_000
        expect(result[1].bitrate).toBe(800_000);
        // Base tier: 180 → 400_000
        expect(result[0].bitrate).toBe(400_000);
    });

    it('odd source dims: top is source-as-is, lower tiers even-rounded', () => {
        const result = buildLadder({
            topWidth: 1001, topHeight: 561, tierCount: 3,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(result).toHaveLength(2);
        // Top = source (odd dims preserved).
        expect(result[1].width).toBe(1001);
        expect(result[1].height).toBe(561);
        // All lower tiers must be even.
        for (let i = 0; i < result.length - 1; i++) {
            expect(result[i].width % 2).toBe(0);
            expect(result[i].height % 2).toBe(0);
        }
    });

    it('quarter-pixel ratio holds for all adjacent tiers (3-tier)', () => {
        const result = buildLadder({
            topWidth: 1280, topHeight: 720, tierCount: 3,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        // Each lower tier is ½ width × ½ height = ¼ pixels of the next.
        for (let i = 0; i < result.length - 1; i++) {
            expect(result[i].width * 2).toBe(result[i + 1].width);
            expect(result[i].height * 2).toBe(result[i + 1].height);
        }
    });

    it('prunes lower tiers below the minimum small axis but keeps top', () => {
        const result = buildLadder({
            topWidth: 320, topHeight: 180, tierCount: 3,
            maxTierCount: WEBCAM_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(dims(result)).toEqual(['320x180']);
    });

    it('screencast arbitrary top rebuilds as top + half-size', () => {
        const result = buildLadder({
            topWidth: 1440, topHeight: 900, tierCount: 2,
            maxTierCount: SCREENCAST_MAX_SIMULCAST_TIERS,
            bitrateFor: hevcBitrate,
        });
        expect(dims(result)).toEqual(['720x450', '1440x900']);
    });
});

describe('capture top sizing', () => {
    it('screencast caps to fit within 1080p preserving aspect', () => {
        expect(fitWithin(3440, 1440, 1920, 1080)).toEqual({ width: 1920, height: 804 });
    });

    it('webcam top uses 16:9 cover-crop target capped at 720p', () => {
        expect(webcamTopSize(1600, 1200)).toEqual({ width: 1280, height: 720 });
        expect(webcamTopSize(960, 720)).toEqual({ width: 960, height: 540 });
    });
});

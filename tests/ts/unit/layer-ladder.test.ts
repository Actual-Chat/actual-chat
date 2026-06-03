import { describe, it, expect } from 'vitest';
import {
    buildLadder,
    fitWithin,
    cameraTopSize,
    fpsForRequestedLayer,
    type LayerConfig,
} from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/layer-ladder';

const CAMERA_BITRATES_KBPS = [162.5, 650, 2_000] as const;
const SCREENCAST_BITRATES_KBPS = [2_187.5, 5_687.5] as const;
const CAMERA_BITRATES = CAMERA_BITRATES_KBPS;
const SCREENCAST_BITRATES = SCREENCAST_BITRATES_KBPS;
const CAMERA_MAX_SIMULCAST_TIERS = CAMERA_BITRATES.length;
const SCREENCAST_MAX_SIMULCAST_TIERS = SCREENCAST_BITRATES.length;

const dims = (ladder: LayerConfig[]): string[] =>
    ladder.map(l => `${l.width}x${l.height}`);

describe('buildLadder — quarter-pixel ratio', () => {
    it('camera 3-tier @ 720p → 180p / 360p / 720p', () => {
        const result = buildLadder({
            topWidth: 1280, topHeight: 720, tierCount: 3,
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
        });
        expect(dims(result)).toEqual(['320x180', '640x360', '1280x720']);
    });

    it('camera 2-tier dropTop fallback @ 360p → 180p / 360p', () => {
        // iOS HW-encoder budget probe-fail path: drop 720p top, keep [180p, 360p].
        const result = buildLadder({
            topWidth: 640, topHeight: 360, tierCount: 2,
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
        });
        expect(dims(result)).toEqual(['160x90', '320x180', '640x360'].slice(-2));
        expect(dims(result)).toEqual(['320x180', '640x360']);
    });

    it('screencast 2-tier @ 1080p → 540p / 1080p', () => {
        const result = buildLadder({
            topWidth: 1920, topHeight: 1080, tierCount: 2,
            maxTierCount: SCREENCAST_MAX_SIMULCAST_TIERS,
            bitratesKbps: SCREENCAST_BITRATES,
        });
        expect(dims(result)).toEqual(['960x540', '1920x1080']);
    });

    it('portrait 720x1280 source — portrait orientation preserved', () => {
        // Source-shaped rebuild for a rotated camera. Each tier is ¼ pixels.
        const result = buildLadder({
            topWidth: 720, topHeight: 1280, tierCount: 3,
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
        });
        expect(dims(result)).toEqual(['180x320', '360x640', '720x1280']);
    });

    it('tierCount cap clamps to CAMERA_MAX_SIMULCAST_TIERS', () => {
        const result = buildLadder({
            topWidth: 1920, topHeight: 1080, tierCount: 10,
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
        });
        expect(result.length).toBeLessThanOrEqual(CAMERA_MAX_SIMULCAST_TIERS);
        expect(CAMERA_MAX_SIMULCAST_TIERS).toBe(3);
    });

    it('tierCount=1 keeps only the top (source dims)', () => {
        const result = buildLadder({
            topWidth: 1280, topHeight: 720, tierCount: 1,
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
        });
        expect(dims(result)).toEqual(['1280x720']);
    });

    it('tierCount=0 returns empty', () => {
        const result = buildLadder({
            topWidth: 1280, topHeight: 720, tierCount: 0,
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
        });
        expect(result).toEqual([]);
    });

    it('zero source dims returns empty (still warming)', () => {
        const result = buildLadder({
            topWidth: 0, topHeight: 0, tierCount: 3,
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
        });
        expect(result).toEqual([]);
    });

    it('uses supplied bottom-first bitrates for each tier', () => {
        const result = buildLadder({
            topWidth: 1280, topHeight: 720, tierCount: 3,
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
        });
        expect(result[2].bitrateKbps).toBe(2_000);
        expect(result[1].bitrateKbps).toBe(650);
        expect(result[0].bitrateKbps).toBe(162.5);
    });

    it('odd source dims: top is source-as-is, lower tiers even-rounded', () => {
        const result = buildLadder({
            topWidth: 1001, topHeight: 561, tierCount: 3,
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
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
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
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
            maxTierCount: CAMERA_MAX_SIMULCAST_TIERS,
            bitratesKbps: CAMERA_BITRATES,
        });
        expect(dims(result)).toEqual(['320x180']);
    });

    it('screencast arbitrary top rebuilds as top + half-size', () => {
        const result = buildLadder({
            topWidth: 1440, topHeight: 900, tierCount: 2,
            maxTierCount: SCREENCAST_MAX_SIMULCAST_TIERS,
            bitratesKbps: SCREENCAST_BITRATES,
        });
        expect(dims(result)).toEqual(['720x450', '1440x900']);
    });
});

describe('buildLadder — explicit tierSizes (append model)', () => {
    const SIZES_4 = [
        { width: 320, height: 180 },
        { width: 640, height: 360 },
        { width: 1280, height: 720 },
        { width: 1920, height: 1080 },
    ] as const;
    const BITRATES_4 = [312.5, 1_250, 4_000, 6_000] as const;

    it('4-tier 180/360/720/1080 with bottom-first bitrates', () => {
        const result = buildLadder({
            topWidth: 1920, topHeight: 1080, tierCount: 4,
            maxTierCount: 4,
            bitratesKbps: BITRATES_4,
            tierSizes: SIZES_4,
        });
        expect(dims(result)).toEqual(['320x180', '640x360', '1280x720', '1920x1080']);
        expect(result.map(l => l.bitrateKbps)).toEqual([312.5, 1_250, 4_000, 6_000]);
        expect(result.map(l => l.baseBitrateKbps)).toEqual([312.5, 1_250, 4_000, 6_000]);
    });

    it('cap below length keeps top tiers, drops bottom, pairs bitrate from top', () => {
        const result = buildLadder({
            topWidth: 1920, topHeight: 1080, tierCount: 3,
            maxTierCount: 4,
            bitratesKbps: BITRATES_4,
            tierSizes: SIZES_4,
        });
        expect(dims(result)).toEqual(['640x360', '1280x720', '1920x1080']);
        expect(result.map(l => l.bitrateKbps)).toEqual([1_250, 4_000, 6_000]);
    });

    it('bypasses ½-derivation — top step is ×1.5 (720→1080), not ×2', () => {
        const result = buildLadder({
            topWidth: 1920, topHeight: 1080, tierCount: 4,
            maxTierCount: 4,
            bitratesKbps: BITRATES_4,
            tierSizes: SIZES_4,
        });
        expect(result[3].width / result[2].width).toBeCloseTo(1.5);
    });
});

describe('capture top sizing', () => {
    it('screencast caps to fit within 1080p preserving aspect', () => {
        expect(fitWithin(3440, 1440, 1920, 1080)).toEqual({ width: 1920, height: 804 });
    });

    it('camera top uses 16:9 cover-crop target capped at 720p', () => {
        expect(cameraTopSize(1600, 1200)).toEqual({ width: 1280, height: 720 });
        expect(cameraTopSize(960, 720)).toEqual({ width: 960, height: 540 });
    });
});

describe('fpsForRequestedLayer', () => {
    const ladder3: LayerConfig[] = [
        { width: 320, height: 180, bitrateKbps: 312 },
        { width: 640, height: 360, bitrateKbps: 1250 },
        { width: 1280, height: 720, bitrateKbps: 4000 },
    ];

    it('maps distance-from-top to fps (L2 top=30, L1=15, L0=10) for a 3-tier ladder', () => {
        expect(fpsForRequestedLayer(ladder3, 0, 30)).toBe(10);
        expect(fpsForRequestedLayer(ladder3, 1, 30)).toBe(15);
        expect(fpsForRequestedLayer(ladder3, 2, 30)).toBe(30);
    });

    it('returns 0 (idle) when nobody is subscribed', () => {
        expect(fpsForRequestedLayer(ladder3, -1, 30)).toBe(0);
    });

    it('focused top tier of a 2-tier mobile ladder earns full rate (not capped by 360p)', () => {
        const ladder2: LayerConfig[] = [
            { width: 320, height: 180, bitrateKbps: 312 },
            { width: 640, height: 360, bitrateKbps: 1250 },
        ];
        expect(fpsForRequestedLayer(ladder2, 1, 30)).toBe(30); // top → full
        expect(fpsForRequestedLayer(ladder2, 0, 30)).toBe(15); // one below top
    });

    it('single-tier ladder always earns full rate when requested', () => {
        const ladder1: LayerConfig[] = [{ width: 1280, height: 720, bitrateKbps: 4000 }];
        expect(fpsForRequestedLayer(ladder1, 0, 30)).toBe(30);
    });

    it('unknown / out-of-range ladder → full capture rate (no pacing)', () => {
        expect(fpsForRequestedLayer(null, 0, 30)).toBe(30);
        expect(fpsForRequestedLayer(ladder3, 5, 30)).toBe(30);
    });

    it('never exceeds the capture rate', () => {
        expect(fpsForRequestedLayer(ladder3, 2, 24)).toBe(24);
        expect(fpsForRequestedLayer(ladder3, 1, 12)).toBe(12);
    });
});

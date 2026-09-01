import { describe, expect, it } from 'vitest';
import {
    getVideoLayerBitrateKbps,
    getVideoLayerBitratesKbps,
    getVideoLayerByteRate,
    getVideoCodecEfficiency,
    parseVideoCodecKind,
    type VideoCodecConstants,
    VideoCodecKind,
} from '../../../src/nodejs/src/app-constants';

const video = {
    codecDefs: [
        { kind: VideoCodecKind.Unknown, efficiency: 1 },
        { kind: VideoCodecKind.H264, efficiency: 1 },
        { kind: VideoCodecKind.Hevc, efficiency: 2 },
        { kind: VideoCodecKind.Vp9, efficiency: 2.35 },
        { kind: VideoCodecKind.Av1, efficiency: 2.85 },
    ],
} satisfies VideoCodecConstants;

describe('video codec bitrate helpers', () => {
    it('parses common codec strings', () => {
        expect(parseVideoCodecKind('avc1.640028')).toBe(VideoCodecKind.H264);
        expect(parseVideoCodecKind('hvc1.1.6.L120.B0')).toBe(VideoCodecKind.Hevc);
        expect(parseVideoCodecKind('hev1.1.6.L120.B0')).toBe(VideoCodecKind.Hevc);
        expect(parseVideoCodecKind('vp09.00.41.08')).toBe(VideoCodecKind.Vp9);
        expect(parseVideoCodecKind('av01.0.08M.08')).toBe(VideoCodecKind.Av1);
        expect(parseVideoCodecKind('unknown')).toBe(VideoCodecKind.Unknown);
    });

    it('computes layer bitrates from H.264 base bitrates and codec efficiency', () => {
        expect(getVideoCodecEfficiency('avc1.640028', video)).toBe(1);
        expect(getVideoCodecEfficiency('hev1.1.6.L120.B0', video)).toBe(2);
        expect(getVideoLayerBitrateKbps(4_000, 'avc1.640028', video)).toBeCloseTo(4_000, 3);
    });

    it('caps the divisor at 1.4, so efficiency past it buys quality not a smaller stream', () => {
        expect(getVideoLayerBitrateKbps(4_000, 'hev1.1.6.L120.B0', video)).toBeCloseTo(2_857.143, 3);
        expect(getVideoLayerBitrateKbps(4_000, 'vp09.00.41.08', video)).toBeCloseTo(2_857.143, 3);
        expect(getVideoLayerBitrateKbps(4_000, 'av01.0.08M.08', video)).toBeCloseTo(2_857.143, 3);
        expect(getVideoLayerBitratesKbps([312.5, 1_250, 4_000], 'hev1.1.6.L120.B0', video)
            .map(x => Math.round(x))).toEqual([223, 893, 2_857]);
        expect(getVideoLayerByteRate(4_000, 'hev1.1.6.L120.B0', video)).toBe(357_143);
    });
});

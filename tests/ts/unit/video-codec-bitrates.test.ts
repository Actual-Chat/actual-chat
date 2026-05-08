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
        expect(getVideoLayerBitrateKbps(4_000, 'vp09.00.41.08', video)).toBeCloseTo(1_702.128, 3);
        expect(getVideoLayerBitratesKbps([312.5, 1_250, 4_000], 'hev1.1.6.L120.B0', video))
            .toEqual([156.25, 625, 2_000]);
        expect(getVideoLayerByteRate(4_000, 'hev1.1.6.L120.B0', video)).toBe(250_000);
    });
});

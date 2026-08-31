import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({ deviceInfo: { isFirefox: false, isMobile: false } }));
vi.mock('device-info', () => ({ DeviceInfo: mocks.deviceInfo }));

import {
    getEncoderLadder,
    supportsAcceleration,
    type CodecInfo,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support';

function codecInfo(
    category: CodecInfo['category'],
    hardwareSupported: boolean,
    softwareSupported: boolean,
): CodecInfo {
    return {
        name: category,
        codec: category,
        category,
        supported: hardwareSupported || softwareSupported,
        hardwareSupported,
        softwareSupported,
        hardwareAccelerated: hardwareSupported,
    };
}

// Mirrors VideoRecorder.listCodecCandidatesByEfficiency: walk the ladder and
// keep the rungs this device can run.
function pick(infos: CodecInfo[]): string[] {
    const byCategory = new Map(infos.map(i => [i.category, i]));
    const out: string[] = [];
    for (const rung of getEncoderLadder()) {
        const info = byCategory.get(rung.category);
        if (info && supportsAcceleration(info, rung.accel))
            out.push(`${rung.accel === 'prefer-hardware' ? 'hw' : 'sw'}-${rung.category}`);
    }
    return out;
}

describe('encoder ladder', () => {
    beforeEach(() => { mocks.deviceInfo.isFirefox = false; });
    afterEach(() => vi.unstubAllGlobals());

    it('ranks hw-av1 > hw-vp9 > hw-hevc > sw-vp9 > hw-h264 > sw-h264', () => {
        const all = [
            codecInfo('av1', true, true),
            codecInfo('vp9', true, true),
            codecInfo('hevc', true, true),
            codecInfo('h264', true, true),
        ];

        expect(pick(all)).toEqual(['hw-av1', 'hw-vp9', 'hw-hevc', 'sw-vp9', 'hw-h264', 'sw-h264']);
    });

    it('never offers software AV1 or software HEVC', () => {
        const ladder = getEncoderLadder();

        expect(ladder.filter(r => r.accel === 'prefer-software').map(r => r.category))
            .toEqual(['vp9', 'h264']);
    });

    // The machine this was measured on: no VP9 hardware encoder, no HEVC
    // software encoder. Software VP9 must still outrank hardware H.264.
    it('prefers software VP9 over hardware H.264', () => {
        const infos = [
            codecInfo('av1', false, true),   // sw-only AV1 is not a rung
            codecInfo('vp9', false, true),
            codecInfo('hevc', true, false),
            codecInfo('h264', true, true),
        ];

        expect(pick(infos)).toEqual(['hw-hevc', 'sw-vp9', 'hw-h264', 'sw-h264']);
    });

    // Firefox has no HEVC encoder and its H.264 runs ~18 frames behind, so
    // offering either only wastes a probe. VP9 is what it lands on — the floor.
    it('drops every MPEG rung on Firefox', () => {
        mocks.deviceInfo.isFirefox = true;
        const all = [
            codecInfo('av1', true, true),
            codecInfo('vp9', true, true),
            codecInfo('hevc', true, true),
            codecInfo('h264', true, true),
        ];

        expect(getEncoderLadder().map(r => r.category)).not.toContain('h264');
        expect(getEncoderLadder().map(r => r.category)).not.toContain('hevc');
        expect(pick(all)).toEqual(['hw-av1', 'hw-vp9', 'sw-vp9']);
    });

    it('leaves Firefox on software VP9 when it reports no hardware encoder', () => {
        mocks.deviceInfo.isFirefox = true;
        const measured = [
            codecInfo('av1', false, true),   // sw-only: not a rung
            codecInfo('vp9', false, true),
            codecInfo('h264', false, true),  // excluded on Firefox anyway
        ];

        expect(pick(measured)).toEqual(['sw-vp9']);
    });

    it('drops a codec with no usable encoder at all', () => {
        expect(pick([codecInfo('av1', false, false), codecInfo('vp9', false, true)]))
            .toEqual(['sw-vp9']);
    });
});

import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({ deviceInfo: { isFirefox: false } }));
vi.mock('device-info', () => ({ DeviceInfo: mocks.deviceInfo }));

import {
    getEncoderLadder,
    selectEncoderCandidates,
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

// The real selection, not a mirror of it: selectEncoderCandidates is what the
// recorder calls.
function pick(
    infos: CodecInfo[],
    allowed: ReadonlySet<CodecInfo['category']> | null = null,
    preferred: Parameters<typeof selectEncoderCandidates>[2] = null,
): string[] {
    return selectEncoderCandidates(infos, allowed, preferred)
        .map(c => `${c.accel === 'prefer-hardware' ? 'hw' : 'sw'}-${c.info.category}`);
}

describe('encoder ladder', () => {
    beforeEach(() => {
        mocks.deviceInfo.isFirefox = false;
    });

    const everything = (): CodecInfo[] => [
        codecInfo('av1', true, true),
        codecInfo('vp9', true, true),
        codecInfo('hevc', true, true),
        codecInfo('h264', true, true),
    ];

    it('ranks every hardware rung ahead of the one software rung', () => {
        expect(pick(everything()))
            .toEqual(['hw-av1', 'hw-vp9', 'hw-hevc', 'sw-vp9', 'hw-h264']);
    });

    // VP9 is the only codec worth encoding in software: it is the floor every
    // client must decode anyway, software AV1 measured ~18% slower than it at
    // 720p for no extra reach, software H.264 was the slowest encoder seen on
    // any device, and Chromium has no software HEVC at all.
    it('offers exactly one software rung, and it is VP9', () => {
        expect(getEncoderLadder().filter(r => r.accel === 'prefer-software').map(r => r.category))
            .toEqual(['vp9']);
    });

    it('prefers software VP9 over hardware H.264', () => {
        // The Galaxy's split: HEVC hardware-only, VP9 and AV1 software-only.
        const infos = [
            codecInfo('av1', false, true),
            codecInfo('vp9', false, true),
            codecInfo('hevc', true, false),
            codecInfo('h264', true, true),
        ];

        expect(pick(infos)).toEqual(['hw-hevc', 'sw-vp9', 'hw-h264']);
    });

    it('drops every MPEG rung on Firefox', () => {
        mocks.deviceInfo.isFirefox = true;

        expect(pick(everything())).toEqual(['hw-av1', 'hw-vp9', 'sw-vp9']);
    });

    it('leaves Firefox on software VP9, which is all it can encode in real time', () => {
        mocks.deviceInfo.isFirefox = true;
        const measured = [
            codecInfo('av1', false, true),   // software AV1 is not a rung
            codecInfo('vp9', false, true),
            codecInfo('h264', false, true),  // excluded on Firefox anyway
        ];

        expect(pick(measured)).toEqual(['sw-vp9']);
    });

    it('drops a codec the audience cannot decode', () => {
        const allowed = new Set<CodecInfo['category']>(['vp9', 'h264']);

        expect(pick(everything(), allowed)).toEqual(['hw-vp9', 'sw-vp9', 'hw-h264']);
    });

    it('drops a codec measured as too slow for a call', () => {
        // A codec measured as too slow: realtime === false, so no rung of it
        // survives - the case Firefox's H.264 hits in practice.
        const infos = everything().map(i => i.category === 'av1' ? { ...i, realtime: false } : i);

        expect(pick(infos)).toEqual(['hw-vp9', 'hw-hevc', 'sw-vp9', 'hw-h264']);
    });

    it('lets the debug preference outrank the ladder, but not conjure a codec', () => {
        expect(pick(everything(), null, 'h264')[0]).toBe('hw-h264');
        // vp9 is not in the audience set, so preferring it changes nothing.
        const allowed = new Set<CodecInfo['category']>(['av1']);
        expect(pick(everything(), allowed, 'vp9')).toEqual(['hw-av1']);
    });

    it('drops a codec with no usable encoder at all', () => {
        expect(pick([codecInfo('av1', false, false), codecInfo('vp9', false, true)]))
            .toEqual(['sw-vp9']);
    });

    // Software-only AV1 or H.264 is no longer a candidate on any platform.
    it('will not fall back to software AV1 or software H.264', () => {
        const softwareOnly = [
            codecInfo('av1', false, true),
            codecInfo('h264', false, true),
        ];

        expect(pick(softwareOnly)).toEqual([]);
    });
});

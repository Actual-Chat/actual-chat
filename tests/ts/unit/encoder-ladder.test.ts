import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({ deviceInfo: { isFirefox: false, isMobile: false } }));
vi.mock('device-info', () => ({ DeviceInfo: mocks.deviceInfo }));

let sharedSettings: typeof import('shared-settings').SharedSettings | undefined;

function setHost(hostKind: string, appKind: string): void {
    sharedSettings?.update({ hostKind, appKind });
}

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
    beforeEach(async () => {
        mocks.deviceInfo.isFirefox = false;
        mocks.deviceInfo.isMobile = false;
        sharedSettings ??= (await import('shared-settings')).SharedSettings;
        setHost('WebServer', 'Unknown');
    });
    afterEach(() => vi.unstubAllGlobals());

    const everything = (): CodecInfo[] => [
        codecInfo('av1', true, true),
        codecInfo('vp9', true, true),
        codecInfo('hevc', true, true),
        codecInfo('h264', true, true),
    ];

    it('puts every hardware rung ahead of every software one', () => {
        expect(pick(everything()))
            .toEqual(['hw-av1', 'hw-vp9', 'hw-hevc', 'sw-av1', 'sw-vp9', 'hw-h264', 'sw-h264']);
    });

    it('never offers software HEVC — Chromium has no such encoder', () => {
        expect(getEncoderLadder().filter(r => r.accel === 'prefer-software').map(r => r.category))
            .not.toContain('hevc');
    });

    // Software AV1 is the most expensive rung, so it is spent only where there
    // is CPU headroom for it.
    it('withholds software AV1 on mobile', () => {
        mocks.deviceInfo.isMobile = true;

        expect(pick(everything())).not.toContain('sw-av1');
    });

    it('withholds software AV1 on the phone MAUI apps only', () => {
        // The MAUI WebView doesn't always report a mobile user agent, so appKind
        // is what settles it there. Desktop MAUI is as capable as the browser.
        for (const appKind of ['Ios', 'Android']) {
            setHost('MauiApp', appKind);
            expect(pick(everything())).not.toContain('sw-av1');
        }
        for (const appKind of ['MacOS', 'Windows']) {
            setHost('MauiApp', appKind);
            expect(pick(everything())).toContain('sw-av1');
        }
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

        expect(pick(infos)).toEqual(['hw-hevc', 'sw-av1', 'sw-vp9', 'hw-h264', 'sw-h264']);
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
        expect(pick(all)).toEqual(['hw-av1', 'hw-vp9', 'sw-av1', 'sw-vp9']);
    });

    it('leaves desktop Firefox on software AV1 when it reports no hardware encoder', () => {
        mocks.deviceInfo.isFirefox = true;
        const measured = [
            codecInfo('av1', false, true),   // sw-only: not a rung
            codecInfo('vp9', false, true),
            codecInfo('h264', false, true),  // excluded on Firefox anyway
        ];

        expect(pick(measured)).toEqual(['sw-av1', 'sw-vp9']);
    });

    it('reads host identity from SharedSettings when none is passed', async () => {
        const { SharedSettings } = await import('shared-settings');
        SharedSettings.update({ hostKind: 'MauiApp', appKind: 'Android' });

        expect(getEncoderLadder().map(r => `${r.accel}-${r.category}`))
            .not.toContain('prefer-software-av1');

        SharedSettings.update({ hostKind: 'MauiApp', appKind: 'MacOS' });
        expect(getEncoderLadder().map(r => `${r.accel}-${r.category}`))
            .toContain('prefer-software-av1');

        SharedSettings.update({ hostKind: 'WebServer', appKind: 'Unknown' });
    });

    it('drops a codec the audience cannot decode', () => {
        const allowed = new Set<CodecInfo['category']>(['vp9', 'h264']);

        expect(pick(everything(), allowed)).toEqual(['hw-vp9', 'sw-vp9', 'hw-h264', 'sw-h264']);
    });

    it('drops a codec measured as too slow for a call', () => {
        // Firefox's H.264: realtime === false, so no rung of it survives.
        const infos = everything().map(i => i.category === 'av1' ? { ...i, realtime: false } : i);

        expect(pick(infos)).toEqual(['hw-vp9', 'hw-hevc', 'sw-vp9', 'hw-h264', 'sw-h264']);
    });

    it('lets the debug preference outrank the ladder, but not conjure a codec', () => {
        expect(pick(everything(), null, 'h264')[0]).toBe('hw-h264');
        // vp9 is not in the audience set, so preferring it changes nothing.
        const allowed = new Set<CodecInfo['category']>(['av1']);
        expect(pick(everything(), allowed, 'vp9')).toEqual(['hw-av1', 'sw-av1']);
    });

    it('drops a codec with no usable encoder at all', () => {
        expect(pick([codecInfo('av1', false, false), codecInfo('vp9', false, true)]))
            .toEqual(['sw-vp9']);
    });

    it('falls to software VP9 on a mobile browser with no hardware encoder', () => {
        mocks.deviceInfo.isMobile = true;

        expect(pick([codecInfo('av1', false, true), codecInfo('vp9', false, true)]))
            .toEqual(['sw-vp9']);
    });
});

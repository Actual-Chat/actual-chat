import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const deviceInfo = vi.hoisted(() => ({ isFirefox: false }));
vi.mock('device-info', () => ({ DeviceInfo: deviceInfo }));

async function freshEncoderModule(level: 'none' | 'vp9' | 'full') {
    vi.resetModules();
    const { WebCodecsCompat } = await import('web-codecs-compat/init');
    WebCodecsCompat.init({ level, baseUrl: '/dist/libav' });

    return import('web-codecs-compat/vp9-encoder');
}

describe('isVp9Codec', () => {
    afterEach(() => {
        vi.resetModules();
    });

    it('matches the vp09 codec strings the ladder produces', async () => {
        const { isVp9Codec } = await freshEncoderModule('vp9');

        expect(isVp9Codec('vp09.00.41.08')).toBe(true);
        expect(isVp9Codec('vp09.00.10.08')).toBe(true);
        expect(isVp9Codec('vp9')).toBe(true);
    });

    it('does not match other codecs', async () => {
        const { isVp9Codec } = await freshEncoderModule('vp9');

        expect(isVp9Codec('vp8')).toBe(false);
        expect(isVp9Codec('av01.0.04M.08')).toBe(false);
        expect(isVp9Codec('avc1.42E01E')).toBe(false);
        expect(isVp9Codec('')).toBe(false);
    });
});

describe('getVideoEncoderClass', () => {
    const originalVideoEncoder = Reflect.get(globalThis, 'VideoEncoder') as unknown;

    beforeEach(() => {
        Reflect.set(globalThis, 'VideoEncoder', function VideoEncoder() { /* stub */ });
    });

    afterEach(() => {
        if (originalVideoEncoder === undefined)
            Reflect.deleteProperty(globalThis, 'VideoEncoder');
        else
            Reflect.set(globalThis, 'VideoEncoder', originalVideoEncoder);

        vi.resetModules();
    });

    it('leaves every codec native at none', async () => {
        const m = await freshEncoderModule('none');

        expect(m.getVideoEncoderClass('vp09.00.41.08')).toBe(globalThis.VideoEncoder);
        expect(m.getVideoEncoderClass('av01.0.04M.08')).toBe(globalThis.VideoEncoder);
    });

    it('replaces only VP9 at vp9, leaving AV1 and H.264 to the engine', async () => {
        const m = await freshEncoderModule('vp9');

        expect(m.getVideoEncoderClass('vp09.00.41.08')).toBe(m.Vp9Encoder as unknown);
        expect(m.getVideoEncoderClass('av01.0.04M.08')).toBe(globalThis.VideoEncoder);
        expect(m.getVideoEncoderClass('avc1.42E01E')).toBe(globalThis.VideoEncoder);
    });

    it('still routes VP9 through Vp9Encoder at full, where the polyfill would otherwise take it', async () => {
        const m = await freshEncoderModule('full');

        expect(m.getVideoEncoderClass('vp09.00.41.08')).toBe(m.Vp9Encoder as unknown);
    });
});

describe('Vp9Encoder.isConfigSupported', () => {
    afterEach(() => {
        vi.resetModules();
    });

    it('supports VP9 wherever the level replaces the encoder', async () => {
        const { Vp9Encoder } = await freshEncoderModule('vp9');
        const result = await Vp9Encoder.isConfigSupported({ codec: 'vp09.00.41.08', width: 1280, height: 720 });

        expect(result.supported).toBe(true);
    });

    it('supports nothing at none, where it is never selected', async () => {
        const { Vp9Encoder } = await freshEncoderModule('none');
        const result = await Vp9Encoder.isConfigSupported({ codec: 'vp09.00.41.08', width: 1280, height: 720 });

        expect(result.supported).toBe(false);
    });

    it('does not claim codecs it cannot encode', async () => {
        const { Vp9Encoder } = await freshEncoderModule('vp9');
        const result = await Vp9Encoder.isConfigSupported({ codec: 'av01.0.04M.08', width: 1280, height: 720 });

        expect(result.supported).toBe(false);
    });
});

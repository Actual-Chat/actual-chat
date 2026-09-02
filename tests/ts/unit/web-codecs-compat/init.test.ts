import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// The class keeps module-level state (level, loaded classes), so each test that
// changes it re-imports the module rather than sharing one instance.
async function freshCompat() {
    vi.resetModules();

    return import('web-codecs-compat/init');
}

describe('WebCodecsCompat.resolveLevel', () => {
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

    it('resolves auto to full where the engine has no WebCodecs at all', async () => {
        Reflect.deleteProperty(globalThis, 'VideoEncoder');
        const { WebCodecsCompat } = await freshCompat();

        expect(WebCodecsCompat.resolveLevel('auto')).toBe('full');
    });

    it('resolves auto to none wherever WebCodecs is present', async () => {
        const { WebCodecsCompat } = await freshCompat();

        expect(WebCodecsCompat.resolveLevel('auto')).toBe('none');
    });

    it('passes an explicit override through, even against the engine default', async () => {
        const { WebCodecsCompat } = await freshCompat();

        expect(WebCodecsCompat.resolveLevel('none')).toBe('none');
        expect(WebCodecsCompat.resolveLevel('full')).toBe('full');
        expect(WebCodecsCompat.resolveLevel('vp9')).toBe('vp9');
    });
});

describe('WebCodecsCompat.affects', () => {
    afterEach(() => {
        vi.resetModules();
    });

    it('affects nothing at none', async () => {
        const { WebCodecsCompat } = await freshCompat();
        WebCodecsCompat.init({ level: 'none', baseUrl: '/dist/libav' });

        expect(WebCodecsCompat.affects('video-encode')).toBe(false);
        expect(WebCodecsCompat.affects('video-decode')).toBe(false);
        expect(WebCodecsCompat.affects('audio-encode')).toBe(false);
        expect(WebCodecsCompat.affects('audio-decode')).toBe(false);
    });

    it('affects only the video encoder at vp9 — audio and decode stay native', async () => {
        const { WebCodecsCompat } = await freshCompat();
        WebCodecsCompat.init({ level: 'vp9', baseUrl: '/dist/libav' });

        expect(WebCodecsCompat.affects('video-encode')).toBe(true);
        expect(WebCodecsCompat.affects('video-decode')).toBe(false);
        expect(WebCodecsCompat.affects('audio-encode')).toBe(false);
        expect(WebCodecsCompat.affects('audio-decode')).toBe(false);
    });

    it('affects every component at full', async () => {
        const { WebCodecsCompat } = await freshCompat();
        WebCodecsCompat.init({ level: 'full', baseUrl: '/dist/libav' });

        expect(WebCodecsCompat.affects('video-encode')).toBe(true);
        expect(WebCodecsCompat.affects('video-decode')).toBe(true);
        expect(WebCodecsCompat.affects('audio-encode')).toBe(true);
        expect(WebCodecsCompat.affects('audio-decode')).toBe(true);
    });

    it('reports a polyfilled frame realm only at full', async () => {
        const vp9 = await freshCompat();
        vp9.WebCodecsCompat.init({ level: 'vp9', baseUrl: '/dist/libav' });
        expect(vp9.WebCodecsCompat.isPolyfilledRealm).toBe(false);

        const full = await freshCompat();
        full.WebCodecsCompat.init({ level: 'full', baseUrl: '/dist/libav' });
        expect(full.WebCodecsCompat.isPolyfilledRealm).toBe(true);
    });
});

describe('WebCodecsCompat gating', () => {
    afterEach(() => {
        vi.resetModules();
    });

    it('keeps the gate open and fetches nothing for a component the level leaves alone', async () => {
        const { WebCodecsCompat } = await freshCompat();
        WebCodecsCompat.init({ level: 'vp9', baseUrl: '/dist/libav' });

        await WebCodecsCompat.whenReadyFor('audio-decode');

        // Untouched: a realm that only decodes audio must not pay for the wasm.
        expect(WebCodecsCompat.isReady).toBe(true);
        expect(WebCodecsCompat.classes).toBeNull();
    });

    it('ignores a second init that disagrees about the level', async () => {
        const { WebCodecsCompat } = await freshCompat();
        WebCodecsCompat.init({ level: 'full', baseUrl: '/dist/libav' });
        WebCodecsCompat.init({ level: 'none', baseUrl: '/dist/libav' });

        expect(WebCodecsCompat.level).toBe('full');
    });

    it('reports nothing as polyfilled until the classes are installed', async () => {
        const { WebCodecsCompat } = await freshCompat();
        WebCodecsCompat.init({ level: 'full', baseUrl: '/dist/libav' });

        expect(WebCodecsCompat.isPolyfilled({})).toBe(false);
        expect(WebCodecsCompat.isPolyfilled(new Uint8Array(1))).toBe(false);
    });
});

describe('frame dimension helpers', () => {
    afterEach(() => {
        vi.resetModules();
    });

    it('reads display dimensions from a VideoFrame', async () => {
        const { frameWidth, frameHeight } = await freshCompat();
        const frame = { displayWidth: 1280, displayHeight: 720 } as VideoFrame;

        expect(frameWidth(frame)).toBe(1280);
        expect(frameHeight(frame)).toBe(720);
    });

    it('reads width/height from an ImageBitmap, which has no display dimensions', async () => {
        const { frameWidth, frameHeight } = await freshCompat();
        const bitmap = { width: 640, height: 360 } as ImageBitmap;

        expect(frameWidth(bitmap)).toBe(640);
        expect(frameHeight(bitmap)).toBe(360);
    });
});

describe('isWebCodecsLevel', () => {
    afterEach(() => {
        vi.resetModules();
    });

    it('accepts the three levels and rejects anything else', async () => {
        const { isWebCodecsLevel } = await freshCompat();

        expect(isWebCodecsLevel('none')).toBe(true);
        expect(isWebCodecsLevel('vp9')).toBe(true);
        expect(isWebCodecsLevel('full')).toBe(true);
        expect(isWebCodecsLevel('auto')).toBe(false);
        expect(isWebCodecsLevel(null)).toBe(false);
        expect(isWebCodecsLevel('VP9')).toBe(false);
    });
});

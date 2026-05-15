import { afterEach, describe, expect, it, vi } from 'vitest';

async function loadScreenOrientation(options: {
    userAgent: string;
    screenAngle: number;
    windowOrientation?: number;
}) {
    vi.resetModules();

    const windowListeners = new Map<string, () => void>();
    const screenListeners = new Map<string, () => void>();
    vi.stubGlobal('navigator', {
        userAgent: options.userAgent,
        maxTouchPoints: 5,
    });
    vi.stubGlobal('window', {
        orientation: options.windowOrientation,
        addEventListener: (name: string, cb: () => void) => windowListeners.set(name, cb),
        matchMedia: () => ({ matches: true }),
    });
    vi.stubGlobal('screen', {
        orientation: {
            angle: options.screenAngle,
            addEventListener: (name: string, cb: () => void) => screenListeners.set(name, cb),
        },
    });

    const module = await import('../../../../src/nodejs/src/orientation');
    return {
        ScreenOrientation: module.ScreenOrientation,
        screenListeners,
        windowListeners,
    };
}

describe('ScreenOrientation', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
        vi.resetModules();
    });

    it('prefers legacy window.orientation on iOS when screen.orientation is stale', async () => {
        const { ScreenOrientation } = await loadScreenOrientation({
            userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15',
            screenAngle: 0,
            windowOrientation: 90,
        });

        ScreenOrientation.init();

        expect(ScreenOrientation.current).toBe(90);
    });

    it('subscribes to legacy orientationchange even when screen.orientation exists', async () => {
        const { ScreenOrientation, windowListeners } = await loadScreenOrientation({
            userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15',
            screenAngle: 0,
            windowOrientation: 0,
        });

        ScreenOrientation.init();
        (window as unknown as { orientation: number }).orientation = 270;
        windowListeners.get('orientationchange')?.();

        expect(ScreenOrientation.current).toBe(270);
    });
});

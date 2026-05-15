import { afterEach, describe, expect, it, vi } from 'vitest';

async function loadDeviceInfo(navigatorLike: {
    userAgent: string;
    userAgentData?: { mobile: boolean } | null;
    maxTouchPoints?: number;
}) {
    vi.resetModules();
    vi.stubGlobal('navigator', navigatorLike);
    vi.stubGlobal('window', {
        matchMedia: () => ({ matches: true }),
    });
    const module = await import('../../../src/nodejs/src/device-info');
    return module.DeviceInfo;
}

describe('DeviceInfo', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
        vi.resetModules();
    });

    it('detects iPhone as iOS mobile', async () => {
        const deviceInfo = await loadDeviceInfo({
            userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15',
            maxTouchPoints: 5,
        });

        expect(deviceInfo.isIos).toBe(true);
        expect(deviceInfo.isMobile).toBe(true);
    });

    it('detects iPadOS desktop user agent as iOS mobile', async () => {
        const deviceInfo = await loadDeviceInfo({
            userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15) AppleWebKit/605.1.15',
            maxTouchPoints: 5,
        });

        expect(deviceInfo.isIos).toBe(true);
        expect(deviceInfo.isMobile).toBe(true);
    });

    it('does not classify a non-touch Mac as iOS', async () => {
        const deviceInfo = await loadDeviceInfo({
            userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15) AppleWebKit/605.1.15',
            maxTouchPoints: 0,
        });

        expect(deviceInfo.isIos).toBe(false);
        expect(deviceInfo.isMobile).toBe(false);
    });
});

// TODO(AY): cleanup eslint suppressions
/// <reference types="user-agent-data-types" />
const userAgent = navigator.userAgent;
const userAgentLowerCase = userAgent.toLowerCase();
const userAgentData = navigator.userAgentData as { mobile: boolean; } | null;
const isMobile = userAgentData?.mobile
    ?? /android|mobile|phone|webos|iphone|ipad|ipod|blackberry/.test(userAgentLowerCase);
const isChromium = userAgentLowerCase.includes('chrome');

export const DeviceInfo = {
    isMobile: isMobile,
    isAndroid: isMobile && userAgentLowerCase.includes('android'),
    isIos: isMobile && /iphone|ipad|ipod/.test(userAgentLowerCase),
    isChromium: isChromium,
    isWebKit: userAgentLowerCase.includes('webkit') && !isChromium,
    isFirefox: userAgentLowerCase.includes('firefox'),
    isEdge: userAgentLowerCase.includes('edg/'),
    isTouchCapable: (typeof window !== 'undefined'
        && (('ontouchstart' in window) || (navigator.maxTouchPoints > 0))
        && window.matchMedia('(pointer: coarse)').matches),
    hasSafeAreaTop: false,
    hasSafeAreaBottom: false,

    init: function (): void {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        const body = document?.body;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!body)
            return;

        const classList = body.classList;
        if (DeviceInfo.isMobile)
            classList.add('device-mobile');
        else
            classList.add('device-desktop');

        if (DeviceInfo.isAndroid)
            classList.add('device-android');
        if (DeviceInfo.isIos)
            classList.add('device-ios');
        if (DeviceInfo.isChromium)
            classList.add('device-chrome');
        if (DeviceInfo.isEdge)
            classList.add('device-edge');
        if (DeviceInfo.isWebKit)
            classList.add('device-webkit');

        if (DeviceInfo.isTouchCapable)
            classList.add('touch-capable');
        else
            classList.add('touch-incapable');

        const probe = document.createElement('div');
        probe.style.cssText = 'position:fixed;top:0;left:0;pointer-events:none;visibility:hidden;'
            + 'padding-top:env(safe-area-inset-top,0px);'
            + 'padding-bottom:env(safe-area-inset-bottom,0px);';
        document.body.appendChild(probe);
        const cs = getComputedStyle(probe);
        DeviceInfo.hasSafeAreaTop = parseFloat(cs.paddingTop) > 0;
        DeviceInfo.hasSafeAreaBottom = parseFloat(cs.paddingBottom) > 0;
        document.body.removeChild(probe);

        if (DeviceInfo.hasSafeAreaTop) classList.add('has-safe-area-top');
        if (DeviceInfo.hasSafeAreaBottom) classList.add('has-safe-area-bottom');
    }
};

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
    isTouchCapable: (typeof window !== 'undefined' && (('ontouchstart' in window)
        || (navigator.maxTouchPoints > 0))),

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
    }
};

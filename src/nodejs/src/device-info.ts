const userAgent = navigator?.userAgent ?? '';
const userAgentLowerCase = userAgent.toLowerCase();
const userAgentData = navigator['userAgentData'] as { mobile: boolean; };
const isMobile = userAgentData?.mobile === true
    || /android|mobile|phone|webos|iphone|ipad|ipod|blackberry/.test(userAgentLowerCase);
const isChromium = userAgentLowerCase.indexOf('chrome') >= 0;

export const DeviceInfo = {
    isMobile: isMobile,
    isAndroid: isMobile && userAgentLowerCase.indexOf('android') >= 0,
    isIos: isMobile && /iphone|ipad|ipod/.test(userAgentLowerCase),
    isChromium: isChromium,
    isWebKit: userAgentLowerCase.indexOf('webkit') >= 0 && !isChromium,
    isFirefox: userAgentLowerCase.indexOf('firefox') >= 0,
    isEdge: userAgentLowerCase.indexOf('edg/') >= 0,
    isTouchCapable: (('ontouchstart' in window)
        || (navigator['MaxTouchPoints'] as number > 0)
        || (navigator['msMaxTouchPoints'] as number > 0)),

    init: function (): void {
        const body = document?.body;
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

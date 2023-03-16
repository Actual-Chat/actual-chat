const userAgent = navigator?.userAgent ?? '';
const userAgentLowerCase = userAgent.toLowerCase();
const userAgentData = navigator['userAgentData'] as { mobile: boolean; };
const isMobile = userAgentData?.mobile === true
    || /android|mobile|phone|webos|iphone|ipad|ipod|blackberry/.test(userAgentLowerCase);

export const DeviceInfo = {
    isMobile: isMobile,
    isAndroid: isMobile && userAgentLowerCase.indexOf('android') >= 0,
    isIos: isMobile && /iphone|ipad|ipod/.test(userAgentLowerCase),
    isChrome: userAgentLowerCase.indexOf('chrome') >= 0,
    isSafari: userAgentLowerCase.indexOf('safari') >= 0,
    isTouchCapable: (('ontouchstart' in window)
        || (navigator['MaxTouchPoints'] as number > 0)
        || (navigator['msMaxTouchPoints'] as number > 0)),
};

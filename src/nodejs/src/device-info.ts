const userAgent = navigator?.userAgent ?? '';
const userAgentLowerCase = userAgent.toLowerCase();
const userAgentData = navigator['userAgentData'] as { mobile: boolean; };
const isMobile = userAgentData?.mobile === true
    || /android|mobile|phone|webos|iphone|ipad|ipod|blackberry/.test(userAgentLowerCase);
const isChrome = userAgentLowerCase.indexOf('chrome') >= 0;

export const DeviceInfo = {
    isMobile: isMobile,
    isAndroid: isMobile && userAgentLowerCase.indexOf('android') >= 0,
    isIos: isMobile && /iphone|ipad|ipod/.test(userAgentLowerCase),
    isChrome: isChrome,
    isSafari: userAgentLowerCase.indexOf('webkit') >= 0 && !isChrome,
    isFirefox: userAgentLowerCase.indexOf('firefox') >= 0,
    isEdge: userAgentLowerCase.indexOf('edg/') >= 0,
    isTouchCapable: (('ontouchstart' in window)
        || (navigator['MaxTouchPoints'] as number > 0)
        || (navigator['msMaxTouchPoints'] as number > 0)),
};

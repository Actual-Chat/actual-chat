// TODO(AY): cleanup eslint suppressions
/// <reference types="user-agent-data-types" />
interface NavigatorLike {
    userAgent?: string;
    userAgentData?: { mobile: boolean; } | null;
    maxTouchPoints?: number;
}

interface WindowLike {
    matchMedia?: (query: string) => { matches: boolean; };
}

interface BrowserGlobals {
    navigator?: NavigatorLike;
    window?: WindowLike;
    document?: Document;
}

const browserGlobals = globalThis as BrowserGlobals;
const navigatorLike = browserGlobals.navigator;
const touchPointCount = navigatorLike?.maxTouchPoints ?? 0;
const userAgent = navigatorLike?.userAgent ?? '';
const userAgentLowerCase = userAgent.toLowerCase();
const userAgentData = navigatorLike?.userAgentData ?? null;
const isIos = /iphone|ipad|ipod/.test(userAgentLowerCase)
    || (userAgentLowerCase.includes('macintosh') && touchPointCount >= 2);
const isMobile = userAgentData?.mobile
    ?? (isIos || /android|mobile|phone|webos|blackberry/.test(userAgentLowerCase));
const isChromium = userAgentLowerCase.includes('chrome');
const windowLike = browserGlobals.window;

export const DeviceInfo = {
    isMobile: isMobile,
    isAndroid: isMobile && userAgentLowerCase.includes('android'),
    isIos: isIos,
    isMacOS: !isIos && userAgentLowerCase.includes('macintosh'),
    isChromium: isChromium,
    isWebKit: userAgentLowerCase.includes('webkit') && !isChromium,
    isFirefox: userAgentLowerCase.includes('firefox'),
    isEdge: userAgentLowerCase.includes('edg/'),
    isTouchCapable: Boolean(windowLike
        && (('ontouchstart' in windowLike) || touchPointCount >= 1)
        && windowLike.matchMedia?.('(pointer: coarse)').matches),

    // navigator.vibrate exists on desktop Chromium/Firefox and silently does nothing there,
    // so the isMobile check is what makes this mean "there is hardware behind it"
    canVibrate: Boolean(isMobile && navigatorLike && ('vibrate' in navigatorLike)),

    updateBodyClasses: function (): void {
        const body = browserGlobals.document?.body;
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

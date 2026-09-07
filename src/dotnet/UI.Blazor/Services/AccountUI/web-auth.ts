import { DeviceInfo } from 'device-info';
import { getLogs } from 'logging';

const { warnLog } = getLogs('WebAuth');
const encode = encodeURIComponent;

export class WebAuth {
    public static windowTarget = '_blank';
    public static windowFeatures = 'width=600,height=600';
    public static signInPath = '/signIn';
    public static signOutPath = '/signOut';
    public static closeFlowPath = '/fusion/close';
    public static allowPopup = !(DeviceInfo.isMobile || DeviceInfo.isWebKit);
    public static mustRedirectOnPopupBlock = true;

    public static signIn(schema: string): Promise<boolean> {
        const path = schema
            ? this.signInPath + '/' + schema
            : this.signInPath;
        return this.showPopupOrRedirect(path, 'Sign-in');
    }

    public static signOut(): Promise<boolean> {
        return this.showPopupOrRedirect(this.signOutPath, 'Sign-out');
    }

    // Private methods

    /** Resolves to false when the browser blocked the popup and no redirect fallback was made. */
    private static showPopupOrRedirect(path: string, flowName: string): Promise<boolean> {
        if (!this.allowPopup) {
            this.redirect(path, flowName);
            return Promise.resolve(true);
        }

        const closeFlowUrl = this.closeFlowPath + '?flow=' + encode(flowName);
        const returnUrl = new URL(closeFlowUrl, document.baseURI).href;
        const url = path + '?returnUrl=' + encode(returnUrl);
        warnLog?.log(`popup: -> ${url}`);
        const popup = window.open(url, this.windowTarget, this.windowFeatures);
        if (!popup || popup.closed || typeof popup.closed == 'undefined') {
            warnLog?.log('popup: blocked by the browser');
            if (!this.mustRedirectOnPopupBlock)
                return Promise.resolve(false);

            this.redirect(path, flowName);
            return Promise.resolve(true);
        }

        // Wait for the popup to close
        return new Promise<boolean>((resolve) => {
            const interval = setInterval(() => {
                if (!popup.closed)
                    return;
                clearInterval(interval);
                resolve(true);
            }, 200);
        });
    }

    private static redirect(path: string, flowName: string) {
        const redirectUrl = window.location.href;
        const closeFlowUrl = this.closeFlowPath +
            '?flow=' + encode(flowName) +
            '&redirectUrl=' + encode(redirectUrl);
        const returnUrl = new URL(closeFlowUrl, document.baseURI).href;
        const url = new URL(path + '?returnUrl=' + encode(returnUrl), document.baseURI).href;
        warnLog?.log(`redirect: -> ${url}`);
        window.location.href = url;
    }
}

window['FusionAuth'] = WebAuth; // Just in case (compatibility with the older code)

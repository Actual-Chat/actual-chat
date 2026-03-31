import { DeviceInfo } from 'device-info';
import { Log } from 'logging';

const { warnLog } = Log.get('WebAuth');
const encode = encodeURIComponent;

export class WebAuth {
    public static windowTarget = '_blank';
    public static windowFeatures = 'width=600,height=600';
    public static signInPath = '/signIn';
    public static signOutPath = '/signOut';
    public static closeFlowPath = '/fusion/close';
    public static allowPopup = !(DeviceInfo.isMobile || DeviceInfo.isWebKit);
    public static mustRedirectOnPopupBlock = true;

    public static signIn(schema: string, mustExist?: boolean | null): Promise<void> {
        const path = schema
            ? this.signInPath + '/' + schema
            : this.signInPath;
        return this.showPopupOrRedirect(path, 'Sign-in', mustExist);
    }

    public static signOut(): Promise<void> {
        return this.showPopupOrRedirect(this.signOutPath, 'Sign-out');
    }

    // Private methods

    private static showPopupOrRedirect(path: string, flowName: string, mustExist?: boolean | null): Promise<void> {
        if (!this.allowPopup) {
            this.redirect(path, flowName, mustExist);
            return Promise.resolve();
        }

        let closeFlowUrl = this.closeFlowPath + '?flow=' + encode(flowName);
        if (mustExist !== undefined && mustExist !== null)
            closeFlowUrl += '&mustExist=' + (mustExist ? '1' : '0');
        const returnUrl = new URL(closeFlowUrl, document.baseURI).href;
        const url = path + '?returnUrl=' + encode(returnUrl);
        warnLog?.log(`popup: -> ${url}`);
        const popup = window.open(url, this.windowTarget, this.windowFeatures);
        if (!popup || popup.closed || typeof popup.closed == 'undefined') {
            if (this.mustRedirectOnPopupBlock) {
                this.redirect(path, flowName, mustExist);
            }
            else {
                alert('Authentication popup is blocked by the browser. Please allow popups on this website and retry.')
            }
            return Promise.resolve();
        }

        // Wait for the popup to close
        return new Promise<void>((resolve) => {
            const interval = setInterval(() => {
                if (!popup.closed)
                    return;
                clearInterval(interval);
                resolve();
            }, 200);
        });
    }

    private static redirect(path: string, flowName: string, mustExist?: boolean | null) {
        const redirectUrl = window.location.href;
        let closeFlowUrl = this.closeFlowPath +
            '?flow=' + encode(flowName) +
            '&redirectUrl=' + encode(redirectUrl);
        if (mustExist !== undefined && mustExist !== null)
            closeFlowUrl += '&mustExist=' + (mustExist ? '1' : '0');
        const returnUrl = new URL(closeFlowUrl, document.baseURI).href;
        const url = new URL(path + '?returnUrl=' + encode(returnUrl), document.baseURI).href;
        warnLog?.log(`redirect: -> ${url}`);
        window.location.href = url;
    }
}

window['FusionAuth'] = WebAuth; // Just in case (compatibility with the older code)

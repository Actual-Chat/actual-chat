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

    public static signIn(schema: string, isRegister = false): Promise<string | null> {
        const path = schema
            ? this.signInPath + '/' + schema
            : this.signInPath;
        return this.showPopupOrRedirect(path, 'Sign-in', isRegister);
    }

    public static signOut(): Promise<string | null> {
        return this.showPopupOrRedirect(this.signOutPath, 'Sign-out');
    }

    public static consumeSignInError(): string | null {
        try {
            const error = localStorage.getItem('signInError');
            if (error) {
                localStorage.removeItem('signInError');
                return error;
            }
        } catch { /* ignore */ }
        return null;
    }

    // Private methods

    private static showPopupOrRedirect(path: string, flowName: string, isRegister = false): Promise<string | null> {
        if (!this.allowPopup) {
            this.redirect(path, flowName, isRegister);
            return Promise.resolve(null); // Page navigates away, never resolves meaningfully
        }

        // Clear any stale error before opening popup
        try { localStorage.removeItem('signInError'); } catch { /* ignore */ }

        let closeFlowUrl = this.closeFlowPath + '?flow=' + encode(flowName);
        if (isRegister)
            closeFlowUrl += '&register=1';
        const returnUrl = new URL(closeFlowUrl, document.baseURI).href;
        const url = path + '?returnUrl=' + encode(returnUrl);
        warnLog?.log(`popup: -> ${url}`);
        const popup = window.open(url, this.windowTarget, this.windowFeatures);
        if (!popup || popup.closed || typeof popup.closed == 'undefined') {
            if (this.mustRedirectOnPopupBlock) {
                this.redirect(path, flowName, isRegister);
            }
            else {
                alert('Authentication popup is blocked by the browser. Please allow popups on this website and retry.')
            }
            return Promise.resolve(null);
        }

        // Monitor popup and check for errors when it closes
        return new Promise<string | null>((resolve) => {
            const interval = setInterval(() => {
                if (!popup.closed)
                    return;
                clearInterval(interval);
                const error = this.consumeSignInError();
                resolve(error);
            }, 200);
        });
    }

    private static redirect(path: string, flowName: string, isRegister = false) {
        const redirectUrl = window.location.href;
        let closeFlowUrl = this.closeFlowPath +
            '?flow=' + encode(flowName) +
            '&redirectUrl=' + encode(redirectUrl);
        if (isRegister)
            closeFlowUrl += '&register=1';
        const returnUrl = new URL(closeFlowUrl, document.baseURI).href;
        const url = new URL(path + '?returnUrl=' + encode(returnUrl), document.baseURI).href;
        warnLog?.log(`redirect: -> ${url}`);
        window.location.href = url;
    }
}

window['FusionAuth'] = WebAuth; // Just in case (compatibility with the older code)

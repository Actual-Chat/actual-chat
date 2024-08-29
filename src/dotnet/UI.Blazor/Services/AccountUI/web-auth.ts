import { DeviceInfo } from 'device-info';
import { Log } from 'logging';

const { warnLog } = Log.get('WebAuth');
const encode = encodeURIComponent;

export class WebAuth {
    public static windowTarget: string = "_blank";
    public static windowFeatures: string = "width=600,height=600";
    public static signInPath: string = "/signIn";
    public static signOutPath: string = "/signOut";
    public static closePath: string = "/fusion/close";
    public static completePath: string = "/fusion/complete";
    public static allowPopup: boolean = !DeviceInfo.isMobile && !DeviceInfo.isWebKit;
    public static mustRedirectOnPopupBlock: boolean = true;

    public static signIn(schema: string) {
        if (schema === undefined || schema === null || schema === "") {
            this.showPopupOrRedirect(this.signInPath, "Sign-in");
        } else {
            this.showPopupOrRedirect(this.signInPath + "/" + schema, "Sign-in");
        }
    }

    public static signOut() {
        this.showPopupOrRedirect(this.signOutPath, "Sign-out");
    }

    // Private methods

    private static showPopupOrRedirect(path: string, flowName: string) {
        if (!this.allowPopup) {
            this.redirect(path, flowName);
            return;
        }

        const returnUrl = new URL(this.closePath + "?flow=" + encode(flowName), document.baseURI).href;
        const url = path + "?returnUrl=" + encode(returnUrl);
        warnLog.log(`popup: -> ${url}`);
        const popup = window.open(url, this.windowTarget, this.windowFeatures);
        if (!popup || popup.closed || typeof popup.closed == 'undefined') {
            if (this.mustRedirectOnPopupBlock) {
                this.redirect(path, flowName);
            }
            else {
                alert("Authentication popup is blocked by the browser. Please allow popups on this website and retry.")
            }
        }
    }

    private static redirect(path: string, flowName: string) {
        const finalReturnUrl = window.location.href;
        const returnUrl = new URL(this.completePath +
            "?flow=" + encode(flowName) +
            "&returnUrl=" + encode(finalReturnUrl),
            document.baseURI
        ).href;
        let url = new URL(path + "?returnUrl=" + encode(returnUrl), document.baseURI).href;
        warnLog.log(`redirect: -> ${url}`);
        window.location.href = url;
    }
}

window['FusionAuth'] = WebAuth; // Just in case (compatibility with the older code)

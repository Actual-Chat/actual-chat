const encode = encodeURIComponent;

class FusionAuth {
    schemas =  '';
    windowTarget = '_blank';
    windowFeatures = 'width=600,height=600';
    signInPath = '/signIn';
    signOutPath = '/signOut';
    closePath = '/fusion/close';
    enablePopup = true;
    mustRedirectOnPopupBlock = true;

    signIn(schema: string) {
        if (schema === undefined || schema === null || schema === '') {
            this.authPopupOrRedirect(this.signInPath, 'Sign-in');
        } else {
            this.authPopupOrRedirect(this.signInPath + '/' + schema, 'Sign-in');
        }
    }

    signOut() {
        this.authPopupOrRedirect(this.signOutPath, 'Sign-out');
    }

    authPopupOrRedirect(action: string, flowName: string) {
        if (!this.enablePopup) {
            this.authRedirect(action);
            return;
        }

        const redirectUrl = new URL(this.closePath +'?flow=' + encode(flowName), document.baseURI).href;
        const url = action + '?returnUrl=' + encode(redirectUrl);
        const popup = window.open(url, this.windowTarget, this.windowFeatures);
        if (!popup || popup.closed || typeof popup.closed == 'undefined') {
            if (this.mustRedirectOnPopupBlock) {
                this.authRedirect(action);
            }
            else {
                alert('Authentication popup is blocked by the browser. Please allow popups on this website and retry.')
            }
        }
    }

    authRedirect(action: string) {
        const redirectUrl = window.location.href;
        const url = action + '?returnUrl=' + encode(redirectUrl);
        window.location.replace(url);
    }
}

const auth = new FusionAuth();

export default auth;

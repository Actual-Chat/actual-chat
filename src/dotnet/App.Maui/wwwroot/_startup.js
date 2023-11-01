(function() {
    window.App = {
        environment: 'unknown',
        baseUri: null,
        sessionHash: null,
        postMessage: function (data) {
            try {
                if (window.Android) // post message to android webview
                    window.Android.postMessage(data);
                if (window.chrome) // post message to windows webview
                    window.chrome.webview.postMessage(data);
            } catch (e) {
                console.log(e);
            }
        },
        /* Bundle/bulk init */
        whenBundleReady: null,
        markBundleReady: function () {
        },
        isBundleReady: async function() {
            if (!window.App.whenBundleReady)
                return false;

            await window.App.whenBundleReady;
            return true;
        },
        browserInit: async function (apiVersion, baseUri, sessionHash, browserInfoBackendRef, appKind) {
            await window.App.whenBundleReady;
            await window.ui.BrowserInit.init(apiVersion, baseUri, sessionHash, browserInfoBackendRef, appKind);
        },
    };
    window.App.whenBundleReady = new Promise((resolve, _) => {
        window.App.markBundleReady = resolve;
    });

    document.addEventListener("DOMContentLoaded", function (event) {
        // console.log("DOM is fully loaded and parsed");
        try {
            window?.Android?.DOMContentLoaded?.();
        } catch (e) {
            console.log(e);
        }
    });

    // Clear history state in case page reload was invoked
    history.replaceState(null, /* ignored title */ '');
})();

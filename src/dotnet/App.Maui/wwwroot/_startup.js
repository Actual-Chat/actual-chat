(function() {
    window.App = {
        environment: 'unknown',
        baseUri: null,
        sessionHash: null,
        renderMode: 'm',
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
        whenBlazorReady: null,
        whenBundleReady: null,
        markBlazorReady: function () { },
        markBundleReady: function () { },
        isBundleReady: async function() {
            if (!window.App.whenBundleReady)
                return false;

            await window.App.whenBundleReady;
            return true;
        },
        browserInit: async function (hostKind, appKind, apiVersion, baseUri, rpcBaseUri, suppoertedHosts, sessionHash, appConstants, browserInfoBackendRef, clipboardInteropRef) {
            await window.App.whenBundleReady;
            await window.ui.BrowserInit.init(hostKind, appKind, apiVersion, baseUri, rpcBaseUri, suppoertedHosts, sessionHash, appConstants, browserInfoBackendRef, clipboardInteropRef);
        },
    };
    window.App.whenBlazorReady = new Promise((resolve, _) => {
        window.App.markBlazorReady = resolve;
    });
    window.App.whenBundleReady = new Promise((resolve, _) => {
        window.App.markBundleReady = resolve;
    });

    // Clear history state in case page reload was invoked
    history.replaceState(null, /* ignored title */ '');
})();

using Android.Webkit;
using Java.Interop;

namespace ActualChat.ClientApp;

partial class MauiBlazorWebViewHandler
{
    protected override Android.Webkit.WebView CreatePlatformView()
    {
        var webView = base.CreatePlatformView();
        webView.Settings.JavaScriptEnabled = true;
        var cookieManager = CookieManager.Instance!;
        // May be will be required https://stackoverflow.com/questions/2566485/webview-and-cookies-on-android
        //cookieManager.SetAcceptThirdPartyCookies(webView, true);
        var jsInterface = new JavascriptInterface(this, webView);
        webView.AddJavascriptInterface(jsInterface, "Android");
        return webView;
    }

    private class JavascriptInterface : Java.Lang.Object
    {
        private readonly MauiBlazorWebViewHandler _handler;
        private readonly Android.Webkit.WebView _webView;

        public event Action<string> MessageReceived = m => { };

        public JavascriptInterface(MauiBlazorWebViewHandler handler, Android.Webkit.WebView webView)
        {
            _handler = handler;
            _webView = webView;
        }

        [JavascriptInterface]
        [Export("DOMContentLoaded")]
        public void OnDOMContentLoaded()
        {
            _webView.Post(() => { 
                try {
                    var script = $"window.chatApp.initPage('{_handler.BaseUri}')";
                    _webView.EvaluateJavascript(script, null);
                }
                catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
                }
            });
        }

        [JavascriptInterface]
        [Export("postMessage")]
        public void OnPostMessage(string data)
        {
            _webView.Post(() => {
                MessageReceived.Invoke(data);
            });
        }
    }
}

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);
        var baseUri = UrlMapper.BaseUri;
        var sessionId = AppSettings.SessionId;

        platformView.Settings.JavaScriptEnabled = true;
        var cookieManager = CookieManager.Instance!;
        // May be will be required https://stackoverflow.com/questions/2566485/webview-and-cookies-on-android
        cookieManager.SetAcceptCookie(true);
        cookieManager.SetAcceptThirdPartyCookies(platformView, true);
        var sessionCookieValue = $"FusionAuth.SessionId={sessionId}; path=/; secure; samesite=none; httponly";
        cookieManager.SetCookie("https://" + "0.0.0.0", sessionCookieValue);
        cookieManager.SetCookie("https://" + baseUri.Host, sessionCookieValue);
        var jsInterface = new JavascriptInterface(this, platformView);
        platformView.AddJavascriptInterface(jsInterface, "Android");
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
                    var sessionHash = new Session(_handler.AppSettings.SessionId).Hash;
                    var script = $"window.App.initPage('{_handler.UrlMapper.BaseUrl}', '{sessionHash}')";
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

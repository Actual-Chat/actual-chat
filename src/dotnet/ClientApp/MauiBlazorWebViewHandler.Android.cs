using System.Net;
using System.Text;
using System.Text.Json;
using Android.Webkit;
using Java.Interop;

namespace ActualChat.ClientApp;

partial class MauiBlazorWebViewHandler
{
    protected override Android.Webkit.WebView CreatePlatformView()
    {
        var webView = base.CreatePlatformView();
        webView.Settings.JavaScriptEnabled = true;
        webView.AddJavascriptInterface(new JavascriptInterface(this, webView), "Android");
        return webView;
    }

    private class JavascriptInterface : Java.Lang.Object
    {
        private readonly MauiBlazorWebViewHandler _handler;
        private readonly Android.Webkit.WebView _webView;

        public JavascriptInterface(MauiBlazorWebViewHandler handler, Android.Webkit.WebView webView)
        {
            _handler = handler;
            _webView = webView;
        }

        [JavascriptInterface]
        [Export("DOMContentLoaded")]
        public void DOMContentLoaded()
        {
            _webView.Post(() => { 
                try {
                    var script = $"window.initPage('{_handler.BaseUri}')";
                    _webView.EvaluateJavascript(script, null);
                }
                catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
                }
            });
        }
    }
}

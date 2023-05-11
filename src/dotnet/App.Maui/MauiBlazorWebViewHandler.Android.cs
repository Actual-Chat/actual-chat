using Android.Webkit;
using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WebView platformView)
    {
        Tracer.Point(nameof(ConnectHandler));
        Log.LogDebug(nameof(ConnectHandler));

        base.ConnectHandler(platformView);

        platformView.Settings.JavaScriptEnabled = true;

        var jsInterface = new AndroidJSInterface(this, platformView);
        // JavascriptToAndroidInterface methods will be available for invocation in js via 'window.Android' object.
        platformView.AddJavascriptInterface(jsInterface, "Android");
        platformView.SetWebViewClient(
            new WebViewClientOverride(platformView.WebViewClient, AppServices.LogFor<WebViewClientOverride>()));
    }

    private class WebViewClientOverride : WebViewClient
    {
        private WebViewClient Original { get; }
        private ILogger Log { get; }

        public WebViewClientOverride(WebViewClient original, ILogger log)
        {
            Original = original;
            Log = log;
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
            => Original.ShouldOverrideUrlLoading(view, request);

        public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
        {
            var resourceResponse = Original.ShouldInterceptRequest(view, request);
            if (resourceResponse == null)
                return null;

            const string contentTypeKey = "Content-Type";
            var contentType = resourceResponse.ResponseHeaders?[contentTypeKey];
            if (OrdinalEquals(contentType, resourceResponse.MimeType) && OrdinalEquals(contentType, "application/wasm"))
                resourceResponse.ResponseHeaders?.Remove(contentTypeKey);
            return resourceResponse;
        }

        public override void OnPageFinished(WebView? view, string? url)
            => Original.OnPageFinished(view, url);

        public override void DoUpdateVisitedHistory(WebView? view, string? url, bool isReload)
        {
            base.DoUpdateVisitedHistory(view, url, isReload);
            var canGoBack = view!.CanGoBack();
            // It seems at this point we can not trust CanGoBack value, when it's navigated to a new address.
            Log.LogDebug(
                "DoUpdateVisitedHistory: Url: '{Url}', IsReload: '{IsReload}', CanGoBack: '{CanGoBack}'",
                url, isReload, canGoBack);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            Original.Dispose();
        }
    }
}

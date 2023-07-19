using Android.Webkit;
using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WebView platformView)
    {
        Tracer.Point(nameof(ConnectHandler));
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

            var url = request?.Url?.ToString();
            if (url is "https://0.0.0.0/" or "https://0.0.0.0")
                return resourceResponse;

            const string contentTypeKey = "Content-Type";
            const string cacheControlKey = "Cache-Control";
            resourceResponse.ResponseHeaders?.Remove(cacheControlKey);
            resourceResponse.ResponseHeaders?.Add(cacheControlKey, "public, max-age=604800");
            // We see duplicate Content-Type headers at Android
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

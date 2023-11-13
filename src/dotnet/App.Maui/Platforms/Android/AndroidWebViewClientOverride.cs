using Android.Webkit;
using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

public class AndroidWebViewClientOverride(
    WebViewClient original,
    AndroidContentDownloader contentDownloader,
    ILogger log
    ) : WebViewClient
{
    private const string AppHostAddress = "0.0.0.0";

    private WebViewClient Original { get; } = original;
    private ILogger Log { get; } = log;
    private AndroidContentDownloader ContentDownloader { get; } = contentDownloader;

    public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        => Original.ShouldOverrideUrlLoading(view, request);

    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        const string contentTypeKey = "Content-Type";
        const string cacheControlKey = "Cache-Control";

        var requestUrl = request?.Url;
        if (request != null && requestUrl != null
            && OrdinalEquals(requestUrl.Host, AppHostAddress)
            && ContentDownloader.CanHandlePath(requestUrl.EncodedPath)) {
            var (stream, mimeType) = ContentDownloader.OpenInputStream(requestUrl.EncodedPath!);
            if (stream == null)
                return null;

            // Prevent response caching by WebView
            var headers = new Dictionary<string, string>(StringComparer.Ordinal) {
                { cacheControlKey, "no-store, no-cache, max-age=0" },
            };
            return new WebResourceResponse(mimeType, null, 200, "OK", headers, stream);
        }

        var resourceResponse = Original.ShouldInterceptRequest(view, request);
        if (resourceResponse == null)
            return null;

        if (!OrdinalEquals(requestUrl?.Host, AppHostAddress))
            return resourceResponse;

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
        if (disposing)
            Original.Dispose();
    }
}

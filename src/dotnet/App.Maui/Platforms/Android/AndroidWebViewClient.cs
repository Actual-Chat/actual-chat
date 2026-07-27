using Android.Webkit;
using Uri = Android.Net.Uri;
using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

// Extends https://github.com/dotnet/maui/blob/main/src/BlazorWebView/src/Maui/Android/WebKitWebViewClient.cs
public class AndroidWebViewClient(
    WebViewClient original,
    AndroidContentDownloader contentDownloader,
    ILogger log
    ) : WebViewClient
{
    private ILogger Log => log;
    private WebViewClient Original { get; } = original;
    private AndroidContentDownloader ContentDownloader { get; } = contentDownloader;
    private bool IsDisconnected { get; set; }

    public void MarkDisconnected()
        => IsDisconnected = true;

#pragma warning disable CA2215, MA0084
    protected override void Dispose(bool disposing)
    {
        Log.LogDebug("Dispose. Disposing={Disposing}", disposing);
        var original = Original;
        if (disposing && original.IsValid())
            original.Dispose();
        base.Dispose(disposing);
    }
#pragma warning restore CA2215, MA0084

    public override bool OnRenderProcessGone(WebView? view, RenderProcessGoneDetail? detail)
    {
        // NOTE(DF): after removing specifying renderer importance, termination handling api should not be invoked.
        // https://developer.android.com/develop/ui/views/layout/webapps/managing-webview#renderer-importance
        // https://developer.android.com/develop/ui/views/layout/webapps/managing-webview#termination-handle

        var didCrash = detail?.DidCrash() == true;
        var details = $"DidCrash: {didCrash}, RendererPriorityAtExit: {detail?.RendererPriorityAtExit()}, {detail}";
        Log.LogWarning("OnRenderProcessGone: {Details}", details);

        return base.OnRenderProcessGone(view, detail);
    }

    public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
    {
        if (IsDisconnected)
            return false;

        return Original.IfValid()?.ShouldOverrideUrlLoading(view, request) ?? false;
    }

    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        const string contentTypeKey = "Content-Type";
        const string cacheControlKey = "Cache-Control";

        if (IsDisconnected)
            return null;

        var requestUrl = request?.Url;
        if (request != null && requestUrl != null
            && IsAppOrigin(requestUrl)
            && AndroidContentDownloader.CanHandleWebRequestUri(requestUrl.EncodedPath)) {
            var (stream, mimeType) = ContentDownloader.GetWebRequestStream(requestUrl.EncodedPath!);
            if (stream == null)
                return null;

            // Prevent response caching by WebView
            var headers = new Dictionary<string, string>() {
                { cacheControlKey, "no-store, no-cache, max-age=0" },
            };
            return new WebResourceResponse(mimeType, null, 200, "OK", headers, stream);
        }

        var resourceResponse = Original.ShouldInterceptRequest(view, request);
        if (resourceResponse == null)
            return null;

        if (requestUrl?.Host != MauiSettings.LocalHost)
            return resourceResponse;

        resourceResponse.ResponseHeaders?.Remove(cacheControlKey);
        resourceResponse.ResponseHeaders?.Add(cacheControlKey, "public, max-age=604800");
        // We see duplicate Content-Type headers at Android
        resourceResponse.ResponseHeaders?.Remove(contentTypeKey);
        return resourceResponse;
    }

    public override void OnPageFinished(WebView? view, string? url)
    {
        if (IsDisconnected)
            return;

        Original.OnPageFinished(view, url);
    }

    public override void DoUpdateVisitedHistory(WebView? view, string? url, bool isReload)
    {
        if (IsDisconnected)
            return;

        base.DoUpdateVisitedHistory(view, url, isReload);
        var canGoBack = view!.CanGoBack();
        // It seems at this point we can not trust CanGoBack value, when it's navigated to a new address.
        Log.LogDebug(
            "DoUpdateVisitedHistory: Url: '{Url}', IsReload: '{IsReload}', CanGoBack: '{CanGoBack}'",
            url, isReload, canGoBack);
    }

    // Private methods

    private static bool IsAppOrigin(Uri url)
        => url.Scheme == System.Uri.UriSchemeHttps
            && url.Host == MauiSettings.LocalHost
            && url.Port <= 0;
}

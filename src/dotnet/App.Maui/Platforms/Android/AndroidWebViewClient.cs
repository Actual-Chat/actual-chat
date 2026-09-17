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

        var cachedResponse = TryReadFromContentCache(request);
        if (cachedResponse != null)
            return cachedResponse;

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

    private WebResourceResponse? TryReadFromContentCache(IWebResourceRequest? request)
    {
        var url = request?.Url?.ToString();
        var range = request?.RequestHeaders?
            .FirstOrDefault(x => x.Key.Equals("Range", StringComparison.OrdinalIgnoreCase)).Value;
        var startedAt = CpuTimestamp.Now;
        var response = MauiContentRequests.TryHandleCached(url, request?.Method, range);
        if (response == null) {
            var fetch = MauiContentRequests.BeginFetch(url, request?.Method, range);
            ContentCacheLog.DebugLog?.LogDebug("Intercept blocked {Elapsed} on T{ThreadId}, fetch={HasFetch}: {Url}",
                CpuTimestamp.Now - startedAt, Environment.CurrentManagedThreadId, fetch != null, url);
            // A fill answers before the real headers exist, so the one that matters for a
            // CORS-flagged load (a canvas-bound image) has to be replayed from what every
            // own-content host sends - without it such a load fails until the entry is cached.
            return fetch is var (body, mimeType)
                ? new WebResourceResponse(mimeType, null, 200, "OK", CorsHeaders(), body)
                : null;
        }

        ContentCacheLog.DebugLog?.LogDebug("Intercept blocked {Elapsed} on T{ThreadId}, hit: {Url}",
            CpuTimestamp.Now - startedAt, Environment.CurrentManagedThreadId, url);

        try {
            // WebView hands an intercepted body to the renderer undecoded
            if (response.Content.Headers.ContentEncoding.Count != 0) {
                response.Dispose();
                return null;
            }

            var contentType = response.Content.Headers.ContentType;
            // Android appends its own Content-Type from the mimeType/encoding arguments
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(x => !x.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(x => x.Key, x => x.Value.ToDelimitedString(", "), StringComparer.OrdinalIgnoreCase);
            return new WebResourceResponse(
                contentType?.MediaType.NullIfEmpty() ?? "application/octet-stream",
                contentType?.CharSet.NullIfEmpty(),
                (int)response.StatusCode,
                response.ReasonPhrase.NullIfEmpty() ?? "OK",
                headers,
                new ContentResponseStream(response, response.Content.ReadAsStream()));
        }
        catch (Exception e) {
            response.Dispose();
            Log.LogWarning(e, "Cannot serve a cached response: {Url}", url);
            return null;
        }
    }

    private static Dictionary<string, string> CorsHeaders()
        => new(StringComparer.OrdinalIgnoreCase) { ["Access-Control-Allow-Origin"] = "*" };

    private static bool IsAppOrigin(Uri url)
        => url.Scheme == System.Uri.UriSchemeHttps
            && url.Host == MauiSettings.LocalHost
            && url.Port <= 0;
}

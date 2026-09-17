using System.Collections.Concurrent;
using Foundation;
using WebKit;

namespace ActualChat.App.Maui;

/// <summary>
/// Serves the app own <c>content://</c> scheme on every Apple platform Blazor WebView:
/// <c>files/</c> from local files, <c>media/</c> from the content cache. WKWebView refuses
/// a handler for https, so remote media can only reach it this way.
/// </summary>
internal sealed class ContentSchemeHandler : NSObject, IWKUrlSchemeHandler
{
    private const int BufferSize = 64 * 1024;
    // The first chunk is what the WebView paints from, so it has to arrive quickly even on a
    // slow link; past that, bigger chunks only save main-thread hops
    private const int ChunkSize = 256 * 1024;
    private static readonly ILogger Log = StaticLog.For<ContentSchemeHandler>();

    public static readonly ContentSchemeHandler Instance = new();

    private readonly ConcurrentDictionary<IntPtr, byte> _stoppedTasks = new();

    [Export("webView:startURLSchemeTask:")]
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        try {
            var requestUrl = urlSchemeTask.Request?.Url?.AbsoluteString;
            MauiContentRequests.Observe(requestUrl, urlSchemeTask.Request?.HttpMethod);
            if (requestUrl.IsNullOrEmpty()) {
                urlSchemeTask.DidFailWithError(new NSError(new NSString("ContentScheme"), -1));
                return;
            }

            if (ContentResolver.TryGetMediaUrlFromUri(requestUrl, out var mediaUrl)) {
                ServeMedia(urlSchemeTask, mediaUrl);
                return;
            }

            if (!ContentResolver.TryGetFilePathFromUri(requestUrl, out var filePath) || !File.Exists(filePath)) {
                urlSchemeTask.DidFailWithError(new NSError(new NSString("ContentScheme"), 404));
                return;
            }

            var contentType = WebResourceUtils.GetResponseContentTypeOrDefault(filePath);
            var fileInfo = new FileInfo(filePath);
            // Use an HTTP-like response (incl. Content-Type) to ensure WKWebView can properly decode
            // and render images/videos for custom schemes.
            var headers = new NSDictionary(
                new NSString("Content-Type"), new NSString(contentType),
                new NSString("Content-Length"), new NSString(fileInfo.Length.ToString()));
            var response = new NSHttpUrlResponse(urlSchemeTask.Request!.Url!, 200, "HTTP/1.1", headers);
            urlSchemeTask.DidReceiveResponse(response);

            using var stream = File.OpenRead(filePath);
            var buffer = new byte[BufferSize];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
                using var data = NSData.FromArray(buffer.AsSpan(0, read).ToArray());
                urlSchemeTask.DidReceiveData(data);
            }
            urlSchemeTask.DidFinish();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to serve content scheme request.");
            urlSchemeTask.DidFailWithError(new NSError(new NSString("ContentScheme"), -2));
        }
    }

    [Export("webView:stopURLSchemeTask:")]
    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
        => _stoppedTasks.TryAdd(urlSchemeTask.Handle.Handle, default);

    // Private methods

    private void ServeMedia(IWKUrlSchemeTask urlSchemeTask, string mediaUrl)
    {
        // WKWebView calls this on the WebView's thread, so the fetch has to run elsewhere;
        // everything after it must be marshalled back, and dropped once the task was stopped.
        var request = urlSchemeTask.Request!;
        var taskHandle = urlSchemeTask.Handle.Handle;
        _ = Task.Run(async () => {
            HttpResponseMessage? response = null;
            try {
                response = await MauiContentRequests.HandleAsync(mediaUrl, request.HttpMethod, null)
                    .ConfigureAwait(false);
                // Every media URL here is projected, so one the cache declines still has to load
                response ??= await MauiContentRequests.FetchDirect(mediaUrl, request.HttpMethod)
                    .ConfigureAwait(false);
                if (response == null) {
                    await Fail(taskHandle, urlSchemeTask, -3).ConfigureAwait(false);
                    return;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType
                    ?? WebResourceUtils.GetResponseContentTypeOrDefault(mediaUrl);
                var length = response.Content.Headers.ContentLength;
                var headers = new NSMutableDictionary();
                headers[new NSString("Content-Type")] = new NSString(contentType);
                // The projected URL is cross-origin to the page, so a CORS-flagged load
                // (a canvas-bound image) needs what every own-content host sends anyway
                headers[new NSString("Access-Control-Allow-Origin")] = new NSString("*");
                if (length is { } l)
                    headers[new NSString("Content-Length")] = new NSString(l.ToString());
                await using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                // Every hop below is a main-thread round-trip, and on Apple every media URL comes
                // through here - one per 64KB chunk saturated the UI thread as soon as a screen
                // of avatars loaded. Anything within one chunk now costs a single hop.
                var chunk = new byte[ChunkSize];
                var headLength = 0;
                var isComplete = false;
                while (headLength < ChunkSize) {
                    var read = await body.ReadAsync(chunk.AsMemory(headLength)).ConfigureAwait(false);
                    if (read == 0) {
                        isComplete = true;
                        break;
                    }

                    headLength += read;
                }

                var headData = NSData.FromArray(chunk.AsSpan(0, headLength).ToArray());
                var isSent = await Send(taskHandle, () => {
                    urlSchemeTask.DidReceiveResponse(new NSHttpUrlResponse(request.Url!, 200, "HTTP/1.1", headers));
                    using (headData)
                        urlSchemeTask.DidReceiveData(headData);
                    if (isComplete)
                        urlSchemeTask.DidFinish();
                }).ConfigureAwait(false);
                if (!isSent || isComplete)
                    return;

                // The rest streams a chunk per hop
                while (true) {
                    var restRead = await body.ReadAsync(chunk).ConfigureAwait(false);
                    if (restRead == 0)
                        break;

                    var data = NSData.FromArray(chunk.AsSpan(0, restRead).ToArray());
                    if (!await Send(taskHandle, () => {
                            using (data)
                                urlSchemeTask.DidReceiveData(data);
                        }).ConfigureAwait(false))
                        return;
                }
                await Send(taskHandle, urlSchemeTask.DidFinish).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to serve media over the content scheme.");
                await Fail(taskHandle, urlSchemeTask, -4).ConfigureAwait(false);
            }
            finally {
                response?.Dispose();
                _stoppedTasks.TryRemove(taskHandle, out _);
            }
        });
    }

    private Task Fail(IntPtr taskHandle, IWKUrlSchemeTask urlSchemeTask, int code)
        => Send(taskHandle, () => urlSchemeTask.DidFailWithError(new NSError(new NSString("ContentScheme"), code)));

    private async Task<bool> Send(IntPtr taskHandle, Action action)
    {
        // A stopped task must never be called again - WKWebView raises an ObjC exception
        if (_stoppedTasks.ContainsKey(taskHandle))
            return false;

        try {
            var isSent = false;
            await MainThread.InvokeOnMainThreadAsync(() => {
                if (_stoppedTasks.ContainsKey(taskHandle))
                    return;

                action.Invoke();
                isSent = true;
            }).ConfigureAwait(false);
            return isSent;
        }
        catch (Exception e) {
            Log.LogWarning(e, "The content scheme task rejected a callback.");
            return false;
        }
    }

    private static class WebResourceUtils
    {
        public static string GetResponseContentTypeOrDefault(string path)
        {
            try {
                var mimeType = MediaMimeTypes.GetMimeType(path);
                return mimeType.IsNullOrEmpty() ? "application/octet-stream" : mimeType;
            }
            catch {
                return "application/octet-stream";
            }
        }
    }
}

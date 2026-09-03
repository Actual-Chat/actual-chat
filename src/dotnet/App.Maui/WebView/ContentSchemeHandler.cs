// TODO: maybe introduce one more approach like *.Apple.cs for all the apple TFMs?
#if IOS || MACCATALYST || MACOS
using Foundation;
using WebKit;

namespace ActualChat.App.Maui;

/// <summary>
/// Serves <c>content://files/&lt;key&gt;</c> WebView requests from local files resolved
/// through <see cref="ContentResolver"/>; registered on the WKWebViewConfiguration of
/// every Apple platform's Blazor WebView.
/// </summary>
internal sealed class ContentSchemeHandler : NSObject, IWKUrlSchemeHandler
{
    private const int BufferSize = 64 * 1024;
    private static readonly ILogger Log = StaticLog.For<ContentSchemeHandler>();

    public static readonly ContentSchemeHandler Instance = new();

    [Export("webView:startURLSchemeTask:")]
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        try {
            var requestUrl = urlSchemeTask.Request?.Url?.AbsoluteString;
            if (requestUrl.IsNullOrEmpty()) {
                urlSchemeTask.DidFailWithError(new NSError(new NSString("ContentScheme"), -1));
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
    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask) { }

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
#endif

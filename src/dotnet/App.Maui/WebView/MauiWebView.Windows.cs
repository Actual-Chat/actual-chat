using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Platform;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    public WebView2Control WindowsWebView { get; private set; } = null!;

    // Private methods

    public partial void SetPlatformWebView(object platformWebView)
    {
        if (ReferenceEquals(PlatformWebView, platformWebView))
            return;

        PlatformWebView = platformWebView;
        WindowsWebView = (WebView2Control)platformWebView;
        // Fixes a brief "flash" w/ white background on Windows
        WindowsWebView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0); // Transparent
        // NOTE(DF): It seems that this event handler is useless
        // because it's CoreWebView2 is already initialized at this point.
        WindowsWebView.CoreWebView2Initialized += static (sender, _) => {
            var webView = sender.CoreWebView2;
            webView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        };
        var coreWebView2 = WindowsWebView.CoreWebView2;
        coreWebView2.PermissionRequested += OnPermissionRequested;
        // Allow loading images and media from the 'content' scheme.
        // NOTE(DF): Check also https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/overview-features-apis?tabs=dotnetcsharp#custom-scheme-registration-
        var contentSchemeUriFilter = ContentResolver.UriContentScheme + "*";
        coreWebView2.AddWebResourceRequestedFilter(contentSchemeUriFilter, CoreWebView2WebResourceContext.Image);
        coreWebView2.AddWebResourceRequestedFilter(contentSchemeUriFilter, CoreWebView2WebResourceContext.Media);
        coreWebView2.WebResourceRequested += CoreWebView2OnWebResourceRequested;
    }

    private void CoreWebView2OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        // NOTE(DF): There is also another subscriber to the same event inside WinUIWebViewManager.
        // See https://github.com/dotnet/maui/blob/main/src/BlazorWebView/src/Maui/Windows/WinUIWebViewManager.cs#L72
        // See https://github.com/dotnet/maui/blob/main/src/BlazorWebView/src/SharedSource/WebView2WebViewManager.cs#L264
        // But it serves only content for AppOrigin (https://0.0.0.1) and it seems they do not conflict.
        // But I still have some doubts about using 2 subscribers for handling web resource requests
        // and using `args.GetDeferral()`.
        var sUri = args.Request.Uri;
        if (!sUri.OrdinalStartsWith(ContentResolver.UriContentScheme))
            return;

        if (!ContentResolver.TryGetFilePathFromUri(sUri, out var filePath))
            return;

        if (!File.Exists(filePath))
            return;

        try {
            // using var deferral = args.GetDeferral();
            Stream content = File.OpenRead(filePath);
            var contentType = WebResourceUtils.GetResponseContentTypeOrDefault(filePath);
            var headers = WebResourceUtils.GetResponseHeaders(contentType);
            var headersString = WebResourceUtils.GetHeaderString(headers);
            // NOTE(DF): we need to wrap a file stream into a stream that does close the underlying stream when the read is completed.
            // It's necessary to work around an issue with disposing the stream.
            // See https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content?tabs=dotnetcsharp#example-of-handling-the-webresourcerequested-event
            // See https://github.com/MicrosoftEdge/WebView2Feedback/issues/2513
            // See https://github.com/dotnet/maui/pull/9254
            // See https://github.com/dotnet/maui/pull/9645
            content = new SelfClosingStreamWrapper(content);
            args.Response = sender.Environment.CreateWebResourceResponse(content.AsRandomAccessStream(), 200, "OK", headersString);
            // deferral.Complete();
        }
        catch {
            // Intended
        }
    }

    public partial void HardNavigateTo(string url)
        => WindowsWebView.CoreWebView2.Navigate(url);

    private partial Task EvaluateJSInternal(string code)
    {
        var request = new EvaluateJavaScriptAsyncRequest(code);
        WindowsWebView.EvaluateJavaScript(request);
        return request.Task;
    }

    private partial void OnInitializing(object? sender, BlazorWebViewInitializingEventArgs eventArgs) { }
    private partial void OnInitialized(object? sender, BlazorWebViewInitializedEventArgs eventArgs)
    {
        var webView = eventArgs.WebView;
        SetPlatformWebView(webView);
    }

    private static void OnPermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        if (args.PermissionKind != CoreWebView2PermissionKind.Microphone && args.PermissionKind != CoreWebView2PermissionKind.Camera)
            return;

        if (!args.IsUserInitiated)
            return; // use default permission handler for non-user requests

        args.State = CoreWebView2PermissionState.Allow;
        args.Handled = true;
        args.SavesInProfile = true;
    }

    private partial void OnLoaded(object? sender, EventArgs eventArgs) { }

    private partial void SetupSessionCookie(Session session)
    {
        var webView = WindowsWebView.CoreWebView2;
        var cookieName = Constants.Session.CookieName;

        var cookie = webView.CookieManager.CreateCookie(cookieName, session.Id, MauiSettings.LocalHost, "/");
        webView.CookieManager.AddOrUpdateCookie(cookie);

        cookie = webView.CookieManager.CreateCookie(cookieName, session.Id, MauiSettings.Host, "/");
        cookie.SameSite = CoreWebView2CookieSameSiteKind.None;
        cookie.IsHttpOnly = true;
        cookie.IsSecure = true;
        webView.CookieManager.AddOrUpdateCookie(cookie);
    }

    // Nested types
    private static class WebResourceUtils
    {
        private static readonly Func<string, string> GetResponseContentTypeOrDefaultFunc;

        static WebResourceUtils()
        {
            var assembly = typeof(BlazorWebViewHandler).Assembly;
 #pragma warning disable IL2026
            var type = assembly.GetType("Microsoft.AspNetCore.Components.WebView.Maui.StaticContentProvider")!;
#pragma warning restore IL2026
 #pragma warning disable IL2075
            var methodInfo = type.GetMethod("GetResponseContentTypeOrDefault", BindingFlags.Static | BindingFlags.NonPublic)!;
 #pragma warning restore IL2075
            GetResponseContentTypeOrDefaultFunc = (Func<string, string>)methodInfo.CreateDelegate(typeof(Func<string, string>));
        }

        public static string GetResponseContentTypeOrDefault(string path)
            => GetResponseContentTypeOrDefaultFunc.Invoke(path);

        public static IDictionary<string, string> GetResponseHeaders(string contentType)
            => new Dictionary<string, string>(StringComparer.Ordinal) {
                { "Content-Type", contentType },
                { "Cache-Control", "no-cache, max-age=0, must-revalidate, no-store" },
            };

        public static string GetHeaderString(IDictionary<string, string> headers) =>
            string.Join(Environment.NewLine, headers.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
    }

    private class SelfClosingStreamWrapper : Stream
    {
        private readonly Stream _inner;

        public SelfClosingStreamWrapper(Stream stream)
            => _inner = stream;

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Flush()
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => _inner.Seek(offset, origin);

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read;
            try {
                read = _inner.Read(buffer, offset, count);
                if (read == 0)
                    _inner.Dispose();
            }
            catch {
                _inner.Dispose();
                throw;
            }
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}

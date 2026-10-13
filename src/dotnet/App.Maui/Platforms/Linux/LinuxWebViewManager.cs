using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;
using WebKit;
using Dispatcher = Microsoft.AspNetCore.Components.Dispatcher;

namespace ActualChat.App.Maui;

// TODO(maui-labs): back to the labs GtkBlazorWebView once its origin is configurable; its other
// gaps - URL queries, response headers - are fixed or easy to fix upstream.

/// <summary>
/// The Blazor Hybrid transport and content source of the GTK app's WebKit view. Stands in for
/// the labs <c>GtkBlazorWebView</c>, which serves the page from app://localhost: the server
/// accepts WebView connections from the unroutable 0.0.0.1 only, as every other platform uses.
/// </summary>
internal sealed class LinuxWebViewManager : WebViewManager
{
    private const string Scheme = "app";
    // window.external is the transport blazor.webview.js talks through, in both directions;
    // index.html loads Blazor with autostart="false" and waits for the Blazor.start() call.
    private const string MessageHandlerName = "webwindowinterop";
    private const string BlazorInitScript = $$"""
        window.__receiveMessageCallbacks = [];
        window.__dispatchMessageCallback = function(message) {
            window.__receiveMessageCallbacks.forEach(function(callback) { callback(message); });
        };
        window.external = {
            sendMessage: function(message) {
                window.webkit.messageHandlers.{{MessageHandlerName}}.postMessage(message);
            },
            receiveMessage: function(callback) {
                window.__receiveMessageCallbacks.push(callback);
            }
        };
        Blazor.start();
        """;

    private static readonly Uri AppOriginUri = MauiWebView.BaseLocalUri;
    private static LinuxWebViewManager? _current;
    private static bool _isSchemeRegistered;

    private readonly WebKit.WebView _webView;
    private bool _isDisposed;

    // UI thread only, like everything else that touches the WebKit view
    public LinuxWebViewManager(
        WebKit.WebView webView,
        IServiceProvider services,
        JSComponentConfigurationStore jsComponents,
        string hostPage)
        : base(
            services,
            Dispatcher.CreateDefault(),
            AppOriginUri,
            new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, Path.GetDirectoryName(hostPage)!)),
            jsComponents,
            Path.GetFileName(hostPage))
    {
        _webView = webView;
        var userContentManager = webView.GetUserContentManager();
        UserContentManager.ScriptMessageReceivedSignal.Connect(
            userContentManager,
            (_, args) => MessageReceived(AppOriginUri, args.Value.ToString()),
            after: false,
            detail: MessageHandlerName);
        userContentManager.RegisterScriptMessageHandler(MessageHandlerName, null);
        var initScript = UserScript.New(
            BlazorInitScript,
            UserContentInjectedFrames.TopFrame,
            UserScriptInjectionTime.End,
            null,
            null);
        userContentManager.AddScript(initScript);

        // A web context takes a scheme once, and every WebKit view shares the default context
        _current = this;
        if (_isSchemeRegistered)
            return;

        _isSchemeRegistered = true;
        webView.WebContext!.RegisterUriScheme(Scheme, static request => _current?.HandleSchemeRequest(request));
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _isDisposed = true;
        return base.DisposeAsyncCore();
    }

    // Protected/internal methods

    protected override void NavigateCore(Uri absoluteUri)
        => _webView.LoadUri(absoluteUri.ToString());

    protected override void SendMessage(string message)
    {
        // Always queued, even from the UI thread: an inline call there would overtake the
        // messages queued from the renderer's threads, and Blazor's render batches are ordered
        var script = $"__dispatchMessageCallback(\"{JavaScriptEncoder.Default.Encode(message)}\")";
        GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, () => {
            if (!_isDisposed)
                _ = _webView.EvaluateJavascriptAsync(script);
            return false;
        });
    }

    // Private methods

    private void HandleSchemeRequest(URISchemeRequest request)
    {
        // index.html versions its scripts and stylesheets with ?v=..., which is not a part of the file name
        var url = request.GetUri();
        var queryIndex = url.AsSpan().IndexOfAny('?', '#');
        if (queryIndex >= 0)
            url = url[..queryIndex];

        var mustFallBackToHostPage = !Path.HasExtension(url);
        TryGetResponseContent(url, mustFallBackToHostPage,
            out var statusCode, out var statusMessage, out var content, out var headers);
        using var buffer = new MemoryStream();
        using (content)
            content.CopyTo(buffer);
        using var bytes = GLib.Bytes.New(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        using var stream = Gio.MemoryInputStream.NewFromBytes(bytes);
        using var response = URISchemeResponse.New(stream, buffer.Length);
        // The headers too, not just the content type: WebAssembly.compileStreaming reads it from them
        var responseHeaders = Soup.MessageHeaders.New(Soup.MessageHeadersType.Response);
        foreach (var (name, value) in headers)
            responseHeaders.Append(name, value);
        response.SetHttpHeaders(responseHeaders);
        if (headers.TryGetValue("Content-Type", out var contentType))
            response.SetContentType(contentType);
        response.SetStatus((uint)statusCode, statusMessage);
        request.FinishWithResponse(response);
    }
}

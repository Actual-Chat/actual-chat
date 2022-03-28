using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;
namespace ActualChat.ClientApp;

public partial class MauiBlazorWebViewHandler
{
    /// <inheritdoc />
    protected override WebView2Control CreatePlatformView()
    {
        var webview = new WebView2Control();
        var baseUri = _settings.BaseUri;
        if (!_settings.BaseUri.EndsWith('/'))
            baseUri += '/';
        webview.EnsureCoreWebView2Async().AsTask().ContinueWith((t, state) => {
            var ctrl = (WebView2Control)state!;
#pragma warning disable VSTHRD101 // Avoid unsupported async delegates
            ctrl.CoreWebView2.DOMContentLoaded += async (_, __) => {
                try {
                    await webview.CoreWebView2.ExecuteScriptAsync($"audio.OpusMediaRecorder.origin='{baseUri}';");
                }
                catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
                }
            };
#pragma warning restore VSTHRD101 // Avoid unsupported async delegates
            ctrl.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            ctrl.CoreWebView2.WebResourceRequested += (CoreWebView2 _sender, CoreWebView2WebResourceRequestedEventArgs args) => {
                using var deferral = args.GetDeferral();
                /// wss:// can't be rewrited, so we should change SignalR script on the fly
                /// <see href="https://github.com/MicrosoftEdge/WebView2Feedback/issues/172" />
                /// <see href="https://github.com/MicrosoftEdge/WebView2Feedback/issues/685" />
                var uri = args.Request.Uri;
                if (uri.StartsWith("https://0.0.0.0/api/", StringComparison.Ordinal)) {
                    args.Request.Uri = uri.Replace("https://0.0.0.0/", baseUri, StringComparison.Ordinal);
                    args.Request.Headers.SetHeader("Origin", baseUri);
                    args.Request.Headers.SetHeader("Referer", baseUri);
                    Debug.WriteLine($"webview.WebResourceRequested: rewrited to {args.Request.Uri}");
                }
                else {
                    Debug.WriteLine($"webview.WebResourceRequested: {uri}");
                }
                deferral.Complete();
            };


#if DEBUG
            ctrl.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
            ctrl.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
        }, webview, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.FromCurrentSynchronizationContext());

        return webview;
    }

}

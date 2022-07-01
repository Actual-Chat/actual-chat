using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.ClientApp;

public partial class MauiBlazorWebViewHandler
{
    /// <inheritdoc />
    protected override WebView2Control CreatePlatformView()
    {
        var webview = new WebView2Control();
        webview.EnsureCoreWebView2Async().AsTask().ContinueWith((t, state) => {
            var ctrl = (WebView2Control)state!;
            ctrl.CoreWebView2.DOMContentLoaded += async (_, __) => {
                try {
                    var script = $"window.chatApp.initPage('{this.BaseUri}')";
                    await webview.CoreWebView2.ExecuteScriptAsync(script);
                }
                catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
                }
            };
#pragma warning restore VSTHRD101 // Avoid unsupported async delegates
            ctrl.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            ctrl.CoreWebView2.WebResourceRequested += (CoreWebView2 _, CoreWebView2WebResourceRequestedEventArgs args) => {
                using var deferral = args.GetDeferral();
                /// wss:// can't be rewrited, so we should change SignalR script on the fly
                /// <see href="https://github.com/MicrosoftEdge/WebView2Feedback/issues/172" />
                /// <see href="https://github.com/MicrosoftEdge/WebView2Feedback/issues/685" />
                var uri = args.Request.Uri;
                if (uri.StartsWith("https://0.0.0.0/api/", StringComparison.Ordinal)) {
                    args.Request.Uri = uri.Replace("https://0.0.0.0/", BaseUri, StringComparison.Ordinal);
                    args.Request.Headers.SetHeader("Origin", BaseUri);
                    args.Request.Headers.SetHeader("Referer", BaseUri);
                    Debug.WriteLine($"webview.WebResourceRequested: rewrited to {args.Request.Uri}");
                }
                // workaround of 'This browser or app may not be secure.'
                else if (uri.StartsWith("https://accounts.google.com", StringComparison.Ordinal)) {
                    args.Request.Headers.SetHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.141 Safari/537.36");
                    args.Request.Headers.SetHeader("sec-ch-ua", "\"Chromium\";v=\"98\", \" Not A;Brand\";v=\"99\"");
                }
                else {
                    Debug.WriteLine($"webview.WebResourceRequested: {uri}");
                }
            };
        }, webview, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.FromCurrentSynchronizationContext());

        return webview;
    }
}

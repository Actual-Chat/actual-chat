using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.ClientApp;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WebView2Control platformView)
    {
        base.ConnectHandler(platformView);

        platformView.CoreWebView2Initialized += CoreWebView2Initialized;
    }

    protected override void DisconnectHandler(WebView2Control platformView)
    {
        platformView.CoreWebView2.DOMContentLoaded -= OnDOMContentLoaded;
        platformView.CoreWebView2Initialized -= CoreWebView2Initialized;
        platformView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;

        base.DisconnectHandler(platformView);
    }

    private void CoreWebView2Initialized(WebView2Control sender, Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs args)
    {
        var ctrl = sender.CoreWebView2;

        var cookie = ctrl.CookieManager.CreateCookie("FusionAuth.SessionId", MauiProgram.SessionId, "0.0.0.0", "/");
        ctrl.CookieManager.AddOrUpdateCookie(cookie);
        cookie = ctrl.CookieManager.CreateCookie("FusionAuth.SessionId", MauiProgram.SessionId, new Uri(BaseUri).Host, "/");
        cookie.SameSite = CoreWebView2CookieSameSiteKind.None;
        cookie.IsSecure = true;
        ctrl.CookieManager.AddOrUpdateCookie(cookie);

        ctrl.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

        ctrl.DOMContentLoaded += OnDOMContentLoaded;
        ctrl.WebResourceRequested += OnWebResourceRequested;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "<Pending>")]
    private async void OnDOMContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args)
    {
        try {
            var script = $"window.chatApp.initPage('{this.BaseUri}')";
            await sender.ExecuteScriptAsync(script);
        }
        catch (Exception ex) {
            Debug.WriteLine(ex.ToString());
        }
    }

    private void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        /// wss:// can't be rewrited, so we should change SignalR script on the fly
        /// <see href="https://github.com/MicrosoftEdge/WebView2Feedback/issues/172" />
        /// <see href="https://github.com/MicrosoftEdge/WebView2Feedback/issues/685" />
        var uri = args.Request.Uri;
        if (uri.StartsWith("https://0.0.0.0/api/", StringComparison.Ordinal)) {
            //args.Request.Uri = uri.Replace("https://0.0.0.0/", BaseUri, StringComparison.Ordinal);
            //args.Request.Headers.SetHeader("Origin", BaseUri);
            //args.Request.Headers.SetHeader("Referer", BaseUri);
            //Debug.WriteLine($"webview.WebResourceRequested: rewrited to {args.Request.Uri}");
        }
        // workaround of 'This browser or app may not be secure.'
        else if (uri.StartsWith("https://accounts.google.com", StringComparison.Ordinal)) {
            args.Request.Headers.SetHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.141 Safari/537.36");
            args.Request.Headers.SetHeader("sec-ch-ua", "\"Chromium\";v=\"98\", \" Not A;Brand\";v=\"99\"");
        }
        else {
            Debug.WriteLine($"webview.WebResourceRequested: {uri}");
        }
    }
}

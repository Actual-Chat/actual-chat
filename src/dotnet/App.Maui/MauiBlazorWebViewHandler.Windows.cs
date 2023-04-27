using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WebView2Control platformView)
    {
        Log.LogDebug("MauiBlazorWebViewHandler.ConnectHandler");
        base.ConnectHandler(platformView);

        platformView.CoreWebView2Initialized += CoreWebView2Initialized;
    }

    protected override void DisconnectHandler(WebView2Control platformView)
    {
        platformView.CoreWebView2.DOMContentLoaded -= OnDOMContentLoaded;
        platformView.CoreWebView2Initialized -= CoreWebView2Initialized;

        base.DisconnectHandler(platformView);
    }

    private void CoreWebView2Initialized(WebView2Control sender, Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs args)
    {
        var ctrl = sender.CoreWebView2;
        var baseUri = AppSettings.BaseUri;
        var sessionId = AppSettings.Session.Id.Value;

        var cookie = ctrl.CookieManager.CreateCookie("FusionAuth.SessionId", sessionId, "0.0.0.0", "/");
        ctrl.CookieManager.AddOrUpdateCookie(cookie);
        cookie = ctrl.CookieManager.CreateCookie("FusionAuth.SessionId", sessionId, baseUri.Host, "/");
        cookie.SameSite = CoreWebView2CookieSameSiteKind.None;
        cookie.IsSecure = true;
        ctrl.CookieManager.AddOrUpdateCookie(cookie);

        ctrl.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

        ctrl.DOMContentLoaded += OnDOMContentLoaded;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "<Pending>")]
    private async void OnDOMContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args)
    {
        try {
            var sessionHash = AppSettings.Session.Hash;
            var script = $"window.App.initPage('{AppSettings.BaseUrl}', '{sessionHash}')";
            await sender.ExecuteScriptAsync(script);
        }
        catch (Exception ex) {
            Debug.WriteLine(ex.ToString());
        }
    }
}

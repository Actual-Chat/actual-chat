using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

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

        base.DisconnectHandler(platformView);
    }

    private void CoreWebView2Initialized(WebView2Control sender, Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs args)
    {
        var ctrl = sender.CoreWebView2;

        var cookie = ctrl.CookieManager.CreateCookie("FusionAuth.SessionId", SessionId, "0.0.0.0", "/");
        ctrl.CookieManager.AddOrUpdateCookie(cookie);
        cookie = ctrl.CookieManager.CreateCookie("FusionAuth.SessionId", SessionId, new Uri(BaseUri).Host, "/");
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
            var script = $"window.chatApp.initPage('{this.BaseUri}')";
            await sender.ExecuteScriptAsync(script);
        }
        catch (Exception ex) {
            Debug.WriteLine(ex.ToString());
        }
    }
}

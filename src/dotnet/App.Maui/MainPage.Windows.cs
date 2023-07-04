using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

public partial class MainPage
{
    public WebView2Control? PlatformWebView { get; private set; }

    public partial void SetupSessionCookie(Uri baseUri, Session session)
    {
        var sessionId = session.Id.Value;
        var ctrl = PlatformWebView!.CoreWebView2;
        var cookie = ctrl.CookieManager.CreateCookie("FusionAuth.SessionId", sessionId, AppHostAddress, "/");
        ctrl.CookieManager.AddOrUpdateCookie(cookie);
        cookie = ctrl.CookieManager.CreateCookie("FusionAuth.SessionId", sessionId, baseUri.Host, "/");
        cookie.SameSite = CoreWebView2CookieSameSiteKind.None;
        cookie.IsHttpOnly = true;
        cookie.IsSecure = true;
        ctrl.CookieManager.AddOrUpdateCookie(cookie);
    }

    private partial void OnWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    { }

    private partial void OnWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        => PlatformWebView = e.WebView;

    private partial void OnWebViewLoaded(object? sender, EventArgs e)
    { }
}

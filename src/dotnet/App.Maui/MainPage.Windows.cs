using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Maui.Platform;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

public partial class MainPage
{
    public WebView2Control? PlatformWebView { get; private set; }

    public partial void SetupSessionCookie(Session session)
    {
        var webView = PlatformWebView!.CoreWebView2;
        var cookieName = Constants.Session.CookieName;
        var sessionId = session.Id.Value;

        var cookie = webView.CookieManager.CreateCookie(cookieName, sessionId, MauiSettings.LocalHost, "/");
        webView.CookieManager.AddOrUpdateCookie(cookie);

        cookie = webView.CookieManager.CreateCookie(cookieName, sessionId, MauiSettings.Host, "/");
        cookie.SameSite = CoreWebView2CookieSameSiteKind.None;
        cookie.IsHttpOnly = true;
        cookie.IsSecure = true;
        webView.CookieManager.AddOrUpdateCookie(cookie);
    }

    public partial void NavigateTo(string url)
    {
        var js = $"window.location.replace({SystemJsonSerializer.Default.Write(url)})";
        PlatformWebView!.EvaluateJavaScript(new EvaluateJavaScriptAsyncRequest(js));
        // PlatformWebView!.CoreWebView2.Navigate(url);
    }

    private partial void OnWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    { }

    private partial void OnWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        => PlatformWebView = e.WebView;

    private partial void OnWebViewLoaded(object? sender, EventArgs e)
    { }
}

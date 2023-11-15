using Microsoft.AspNetCore.Components.WebView;
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
        WindowsWebView.CoreWebView2Initialized += static (sender, _) => {
            var webView = sender.CoreWebView2;
            webView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        };
    }

    public partial void HardNavigateTo(string url)
        => WindowsWebView.CoreWebView2.Navigate(url);

    public partial Task EvaluateJavaScript(string javaScript)
    {
        var request = new EvaluateJavaScriptAsyncRequest(javaScript);
        WindowsWebView.EvaluateJavaScript(request);
        return request.Task;
    }

    private partial void OnInitializing(object? sender, BlazorWebViewInitializingEventArgs eventArgs) { }
    private partial void OnInitialized(object? sender, BlazorWebViewInitializedEventArgs eventArgs) { }
    private partial void OnLoaded(object? sender, EventArgs eventArgs) { }

    private partial void SetupSessionCookie(Session session)
    {
        var webView = WindowsWebView.CoreWebView2;
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
}

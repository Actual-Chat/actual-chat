using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Maui.Platform;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    public WebView2Control WindowsWebView { get; } = (WebView2Control)platformWebView;

    public partial void OnHandlerConnected()
        => WindowsWebView.CoreWebView2Initialized += static (sender, _) => {
            var webView = sender.CoreWebView2;
            webView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        };

    public partial void OnHandlerDisconnected() { }
    public partial void OnInitializing(BlazorWebViewInitializingEventArgs eventArgs) { }
    public partial void OnInitialized(BlazorWebViewInitializedEventArgs eventArgs) { }
    public partial void OnLoaded(EventArgs eventArgs) { }

    public partial Task EvaluateJavaScript(string javaScript)
    {
        var request = new EvaluateJavaScriptAsyncRequest(javaScript);
        WindowsWebView.EvaluateJavaScript(request);
        return request.Task;
    }

    // Private methods

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

using Android.Webkit;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Maui.Platform;
using Color = Android.Graphics.Color;
using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    public WebView AndroidWebView { get; private set; } = null!;

    public partial void SetPlatformWebView(object platformWebView)
    {
        if (ReferenceEquals(PlatformWebView, platformWebView))
            return;

        PlatformWebView = platformWebView;
        AndroidWebView = (WebView)platformWebView;
        AndroidWebView.SetBackgroundColor(Color.Transparent);
    }

    public partial void HardNavigateTo(string url)
        => AndroidWebView.LoadUrl(url);

    public partial Task EvaluateJavaScript(string javaScript)
    {
        var request = new EvaluateJavaScriptAsyncRequest(javaScript);
        AndroidWebView.EvaluateJavaScript(request);
        return request.Task;
    }

    // Private methods

    private partial void OnInitializing(object? sender, BlazorWebViewInitializingEventArgs eventArgs)
    { }

    private partial void OnInitialized(object? sender, BlazorWebViewInitializedEventArgs eventArgs)
        => SetPlatformWebView(eventArgs.WebView);

    private partial void OnLoaded(object? sender, EventArgs eventArgs) { }

    private partial void SetupSessionCookie(Session session)
    {
        var webView = AndroidWebView;
        if (webView.IsNull())
            return;

        var cookieManager = CookieManager.Instance!;
        var cookieName = Constants.Session.CookieName;
        var sessionId = session.Id.Value;

        // May be will be required https://stackoverflow.com/questions/2566485/webview-and-cookies-on-android
        cookieManager.SetAcceptCookie(true);
        cookieManager.SetAcceptThirdPartyCookies(AndroidWebView, true);
        var sessionCookieValue = $"{cookieName}={sessionId}; path=/; secure; samesite=none; httponly";
        cookieManager.SetCookie("https://" + MauiSettings.LocalHost, sessionCookieValue);
        cookieManager.SetCookie("https://" + MauiSettings.Host, sessionCookieValue);
    }
}

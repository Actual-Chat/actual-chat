using ActualChat.Module;
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

    private partial Task EvaluateJSInternal(string code)
    {
        var request = new EvaluateJavaScriptAsyncRequest(code);
        AndroidWebView.EvaluateJavaScript(request);
        return request.Task;
    }

    // Private methods

    private partial void OnInitializing(object? sender, BlazorWebViewInitializingEventArgs eventArgs)
    { }

    private partial void OnInitialized(object? sender, BlazorWebViewInitializedEventArgs eventArgs)
        => SetPlatformWebView(eventArgs.WebView);

    private partial void OnLoaded(object? sender, EventArgs eventArgs) { }

    private partial async Task SetupCookies(Session session)
    {
        using var webView = AndroidWebView.Hold();
        if (!webView.IsValid)
            return;

        var cookieManager = CookieManager.Instance!;
        var url = "https://" + MauiSettings.Host;
        // May be will be required https://stackoverflow.com/questions/2566485/webview-and-cookies-on-android
        cookieManager.SetAcceptCookie(true);
        cookieManager.SetAcceptThirdPartyCookies(AndroidWebView, true);

        await SetCookie(Constants.Session.CookieName, session.Id, isHttpOnly: true).ConfigureAwait(true);
        await SetCookie("GCLB", $"\"{AppLoadBalancerSettings.Instance.RouteId}\"", isHttpOnly: false).ConfigureAwait(true);
        return;

        Task SetCookie(string name, string value, bool isHttpOnly) {
            // Without a domain this is a host-only cookie, so it never reaches the edge
            // hosts the RPC connection can move to and those connections arrive with no
            // session. The token path that would avoid cookies is iOS/macOS-only, and
            // deliberately so: a cookie the WebView cannot read is worth keeping.
            var domain = MauiSettings.Host;
            var attributes = isHttpOnly
                ? $"domain={domain}; path=/; secure; samesite=none; httponly"
                : $"domain={domain}; path=/; secure; samesite=none";
            var taskSource = TaskCompletionSourceExt.New();
            cookieManager.SetCookie(url, $"{name}={value}; {attributes}", new CookieSetValueCallback(taskSource));
            return taskSource.Task;
        }
    }

    // Nested types

    private sealed class CookieSetValueCallback(TaskCompletionSource taskSource) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value)
            => taskSource.SetResult();
    }
}

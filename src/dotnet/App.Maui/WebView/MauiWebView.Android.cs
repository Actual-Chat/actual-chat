using Android.Webkit;
using AndroidX.Activity;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Maui.Platform;
using WebView = Android.Webkit.WebView;
using MixedContentHandling = Android.Webkit.MixedContentHandling;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    public WebView AndroidWebView { get; } = (WebView)platformWebView;

    // Private methods

    public partial void OnHandlerConnected()
    {
        AndroidWebView.Settings.JavaScriptEnabled = true;
        var jsInterface = new AndroidJSInterface(AndroidWebView);
        // JavascriptToAndroidInterface methods will be available for invocation in js via 'window.Android' object.
        AndroidWebView.AddJavascriptInterface(jsInterface, "Android");
        AndroidWebView.SetWebViewClient(
            new AndroidWebViewClientOverride(
                AndroidWebView.WebViewClient,
                AppServices.GetRequiredService<AndroidContentDownloader>(),
                AppServices.LogFor<AndroidWebViewClientOverride>()));
    }

    public partial void OnHandlerDisconnected() { }
    public partial void OnInitializing(BlazorWebViewInitializingEventArgs eventArgs) { }

    public partial void OnInitialized(BlazorWebViewInitializedEventArgs eventArgs)
    {
        var webView = AndroidWebView;
        if (webView.Context?.GetActivity() is not ComponentActivity activity)
            throw StandardError.Constraint(
                $"The permission-managing WebChromeClient requires that the current activity is a '{nameof(ComponentActivity)}'.");

        var settings = webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.AllowFileAccess = true;
        settings.MediaPlaybackRequiresUserGesture = false;
        settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
        settings.CacheMode = CacheModes.Default;
        // settings.OffscreenPreRaster = true;
 #pragma warning disable CS0618
        settings.EnableSmoothTransition();
 #pragma warning restore CS0618

        // settings.SetGeolocationEnabled(true);
        // settings.SetGeolocationDatabasePath(webView.Context?.FilesDir?.Path);
        webView.SetWebChromeClient(new AndroidWebChromeClient(
            webView.WebChromeClient!,
            activity,
            new AndroidFileChooser(AppServicesAccessor.AppServices.LogFor<AndroidFileChooser>())));
        webView.SetRendererPriorityPolicy(RendererPriority.Important, true);
    }

    public partial void OnLoaded(EventArgs eventArgs) { }

    // Private methods

    private partial void SetupSessionCookie(Session session)
    {
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

    private partial Task EvaluateJavaScript(string javaScript)
    {
        var request = new EvaluateJavaScriptAsyncRequest(javaScript);
        AndroidWebView.EvaluateJavaScript(request);
        return request.Task;
    }
}

using Android.Views;
using Android.Webkit;
using AndroidX.Activity;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Maui.Platform;
using AWebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

public partial class MainPage
{
    /// <summary>
    /// Gets the <see cref="AWebView"/> instance that was initialized.
    /// </summary>
    public AWebView? PlatformWebView { get; private set; }

    public partial void SetupSessionCookie(Session session)
    {
        var cookieManager = CookieManager.Instance!;
        var cookieName = Constants.Session.CookieName;
        var sessionId = session.Id.Value;

        // May be will be required https://stackoverflow.com/questions/2566485/webview-and-cookies-on-android
        cookieManager.SetAcceptCookie(true);
        cookieManager.SetAcceptThirdPartyCookies(PlatformWebView, true);
        var sessionCookieValue = $"{cookieName}={sessionId}; path=/; secure; samesite=none; httponly";
        cookieManager.SetCookie("https://" + MauiSettings.LocalHost, sessionCookieValue);
        cookieManager.SetCookie("https://" + MauiSettings.Host, sessionCookieValue);
    }

    // Example to control permissions in browser is taken from the comment
    // https://github.com/dotnet/maui/issues/4768#issuecomment-1137906982
    // https://github.com/MackinnonBuck/MauiBlazorPermissionsExample
    // In the future, they hope to provide a framework-integrated solution
    // that doesn't require individual apps to worry about configuring WebView options
    // and handling permission requests.

    // To manage Android permissions, update AndroidManifest.xml to include the permissions and
    // features required by your app. You may have to perform additional configuration to enable
    // use of those APIs from the WebView, as is done below. A custom WebChromeClient is needed
    // to define what happens when the WebView requests a set of permissions. See
    // PermissionManagingBlazorWebChromeClient.cs to explore the approach taken in this example.

    private partial void OnWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    { }

    private partial void OnWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        var webView = PlatformWebView = e.WebView;
        if (webView.Context?.GetActivity() is not ComponentActivity activity)
            throw StandardError.Constraint(
                $"The permission-managing WebChromeClient requires that the current activity is a '{nameof(ComponentActivity)}'.");

        var settings = webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.AllowFileAccess = true;
        settings.MediaPlaybackRequiresUserGesture = false;
        settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
        // settings.OffscreenPreRaster = true;
 #pragma warning disable CS0618
        settings.EnableSmoothTransition();
 #pragma warning restore CS0618

        // settings.SetGeolocationEnabled(true);
        // settings.SetGeolocationDatabasePath(webView.Context?.FilesDir?.Path);
        webView.SetWebChromeClient(new AndroidWebChromeClient(webView.WebChromeClient!, activity));
        webView.SetRendererPriorityPolicy(RendererPriority.Important, true);
    }

    private partial void OnWebViewLoaded(object? sender, EventArgs e)
    { }
}

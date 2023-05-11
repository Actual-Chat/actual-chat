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

    public partial void SetupSessionCookie(Uri baseUri, Session session)
    {
        var cookieManager = CookieManager.Instance!;
        // May be will be required https://stackoverflow.com/questions/2566485/webview-and-cookies-on-android
        cookieManager.SetAcceptCookie(true);
        cookieManager.SetAcceptThirdPartyCookies(PlatformWebView, true);
        var sessionCookieValue = $"FusionAuth.SessionId={session.Id.Value}; path=/; secure; samesite=none; httponly";
        cookieManager.SetCookie("https://" + AppHostAddress, sessionCookieValue);
        cookieManager.SetCookie("https://" + baseUri.Host, sessionCookieValue);
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

    private partial void OnBlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    { }

    private partial void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        PlatformWebView = e.WebView;

        if (e.WebView.Context?.GetActivity() is not ComponentActivity activity)
            throw StandardError.Constraint(
                $"The permission-managing WebChromeClient requires that the current activity is a '{nameof(ComponentActivity)}'.");

        e.WebView.Settings.JavaScriptEnabled = true;
        e.WebView.Settings.AllowFileAccess = true;
        e.WebView.Settings.MediaPlaybackRequiresUserGesture = false;
 #pragma warning disable CS0618
        e.WebView.Settings.EnableSmoothTransition();
 #pragma warning restore CS0618
        //e.WebView.Settings.SetGeolocationEnabled(true);
        //e.WebView.Settings.SetGeolocationDatabasePath(e.WebView.Context?.FilesDir?.Path);
        e.WebView.SetWebChromeClient(new PermissionManagingBlazorWebChromeClient(e.WebView.WebChromeClient!, activity));
    }

    private partial void OnBlazorWebViewLoaded(object? sender, EventArgs e)
    { }
}

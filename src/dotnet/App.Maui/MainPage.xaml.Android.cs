using AndroidX.Activity;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Maui.Platform;

namespace ActualChat.App.Maui;

public partial class MainPage
{
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

    private partial void BlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    { }

    private partial void BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        PlatformWebView = e.WebView;

        if (e.WebView.Context?.GetActivity() is not ComponentActivity activity)
            throw StandardError.Constraint(
                $"The permission-managing WebChromeClient requires that the current activity is a '{nameof(ComponentActivity)}'.");

        e.WebView.Settings.JavaScriptEnabled = true;
        e.WebView.Settings.AllowFileAccess = true;
        e.WebView.Settings.MediaPlaybackRequiresUserGesture = false;
        e.WebView.Settings.EnableSmoothTransition();
        //e.WebView.Settings.SetGeolocationEnabled(true);
        //e.WebView.Settings.SetGeolocationDatabasePath(e.WebView.Context?.FilesDir?.Path);
        e.WebView.SetWebChromeClient(new PermissionManagingBlazorWebChromeClient(e.WebView.WebChromeClient!, activity));
    }
}

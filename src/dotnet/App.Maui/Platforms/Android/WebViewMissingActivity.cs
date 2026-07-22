using Android.App;
using Android.Content;
using Android.OS;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

/// <summary>
/// Shown instead of <see cref="MainActivity"/> when the device has no usable WebView provider:
/// creating <c>BlazorAndroidWebView</c> would crash with <c>MissingWebViewPackageException</c> otherwise.
/// </summary>
[Activity(
    Name = MauiSettings.IsDevApp
        ? "chat.actual.dev.app.WebViewMissingActivity"
        : "actual.chat.app.WebViewMissingActivity",
    Theme = "@style/SplashTheme",
    ExcludeFromRecents = true,
    Exported = false)]
public sealed class WebViewMissingActivity : Android.App.Activity
{
    private const string WebViewPackageId = "com.google.android.webview";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var appName = ApplicationInfo?.LoadLabel(PackageManager!) ?? "Voxt";
        new AlertDialog.Builder(this)
            .SetTitle("Android System WebView is required")!
            .SetMessage(
                $"{appName} can't start because Android System WebView is missing, disabled, or being updated. "
                + $"Install or enable it, then open {appName} again.")!
            .SetCancelable(false)!
            .SetPositiveButton("Get WebView", (_, _) => {
                OpenWebViewInstallPage();
                FinishAffinity();
            })!
            .SetNegativeButton("Close", (_, _) => FinishAffinity())!
            .Show();
    }

    // Private methods

    private void OpenWebViewInstallPage()
    {
        try {
            StartActivity(new Intent(Intent.ActionView, Uri.Parse("market://details?id=" + WebViewPackageId)));
        }
        catch (ActivityNotFoundException) {
            StartActivity(new Intent(
                Intent.ActionView,
                Uri.Parse("https://play.google.com/store/apps/details?id=" + WebViewPackageId)));
        }
    }
}

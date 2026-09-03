using ActualChat.Localization;
using ActualChat.Maui;
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
        var appName = ApplicationInfo?.LoadLabel(PackageManager!) ?? CoreConstants.AppName;
        var l = AppStrings.L;
        new AlertDialog.Builder(this)
            .SetTitle(l.WebViewMissing_Title)!
            .SetMessage(l.WebViewMissing_Message_Format(appName))!
            .SetCancelable(false)!
            .SetPositiveButton(l.WebViewMissing_GetWebView, (_, _) => {
                OpenWebViewInstallPage();
                FinishAffinity();
            })!
            .SetNegativeButton(l.Common_Close, (_, _) => FinishAffinity())!
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

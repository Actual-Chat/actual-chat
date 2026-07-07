using Android.Views;
using Android.Webkit;
using WebView = Android.Webkit.WebView;
using MixedContentHandling = Android.Webkit.MixedContentHandling;
using Microsoft.Maui.Platform;

namespace ActualChat.App.Maui;

public partial class CustomBlazorWebViewHandler
{
    private AndroidWebViewClient? _androidWebViewClient;
    private AndroidWebChromeClient? _androidWebChromeClient;

    protected override WebView CreatePlatformView()
    {
        Log.LogDebug("-> CreatePlatformView");
        Log.LogDebug("-> base.CreatePlatformView");
        var webView = base.CreatePlatformView();
        Log.LogDebug("<- base.CreatePlatformView");

        if (webView.Context!.GetActivity() is not MainActivity mainActivity)
            throw StandardError.Constraint(
                $"The permission-managing WebChromeClient requires that the current activity is a '{nameof(MainActivity)}'.");

        webView.SetLayerType(Android.Views.LayerType.Hardware, null);
        var settings = webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.AllowFileAccess = true;
        settings.MediaPlaybackRequiresUserGesture = false;
        settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
        settings.CacheMode = CacheModes.Default;
        settings.TextZoom = 100;
        // Disable WebView's algorithmic dark theme: the app controls its own
        // theme via CSS, otherwise a light theme gets inverted when the system
        // is in dark mode. API 33+ uses AlgorithmicDarkeningAllowed; earlier
        // versions use the now-deprecated ForceDark.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            settings.AlgorithmicDarkeningAllowed = false;
        else if (OperatingSystem.IsAndroidVersionAtLeast(29)) {
#pragma warning disable CA1422
            settings.ForceDark = ForceDarkMode.Off;
#pragma warning restore CA1422
        }

        // Prevent native scrolling so DOM can handle resizing via interactive-widget=resizes-content
        webView.VerticalScrollBarEnabled = false;
        webView.HorizontalScrollBarEnabled = false;
        webView.OverScrollMode = OverScrollMode.Never;
        webView.ScrollChange += (sender, e) => {
            if (e.ScrollX != 0 || e.ScrollY != 0) {
                webView.ScrollTo(0, 0);
            }
        };

        // settings.OffscreenPreRaster = true;
#pragma warning disable CS0618
        settings.EnableSmoothTransition();
#pragma warning restore CS0618

        var services = MauiContext!.Services;
        _androidWebViewClient = new AndroidWebViewClient(
            webView.WebViewClient,
            services.GetRequiredService<AndroidContentDownloader>(),
            services.LogFor<AndroidWebViewClient>());
        webView.SetWebViewClient(_androidWebViewClient);

        _androidWebChromeClient = new AndroidWebChromeClient(
            webView.WebChromeClient!,
            mainActivity,
            new VisualMediaFileChooser(mainActivity),
            services.LogFor<AndroidWebChromeClient>());
        webView.SetWebChromeClient(_androidWebChromeClient);

        Log.LogDebug("<- CreatePlatformView");
        return webView;
    }

    protected override void DisconnectHandler(WebView platformView)
    {
        Log.LogDebug("-> DisconnectHandler");
        try {
            _androidWebViewClient?.MarkDisconnected();
            _androidWebChromeClient?.MarkDisconnected();
            Log.LogDebug("-> base.DisconnectHandler");
            base.DisconnectHandler(platformView);
            Log.LogDebug("<- base.DisconnectHandler");
            if (platformView.Parent is ViewGroup parent)
                parent.RemoveView(platformView);
            platformView.Destroy();
        }
        catch (Exception e) {
            Log.LogWarning(e, "An error occured during disconnecting Android WebView");
        }
        Log.LogDebug("<- DisconnectHandler");
    }
}

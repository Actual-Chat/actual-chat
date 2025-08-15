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

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= StaticLog.For<CustomBlazorWebViewHandler>();

    protected override WebView CreatePlatformView()
    {
        Log.LogDebug("-> CreatePlatformView");
        Log.LogDebug("-> base.CreatePlatformView");
        var webView = base.CreatePlatformView();
        Log.LogDebug("<- base.CreatePlatformView");

        if (webView.Context!.GetActivity() is not MainActivity mainActivity)
            throw StandardError.Constraint(
                $"The permission-managing WebChromeClient requires that the current activity is a '{nameof(MainActivity)}'.");

        var settings = webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.AllowFileAccess = true;
        settings.MediaPlaybackRequiresUserGesture = false;
        settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
        settings.CacheMode = CacheModes.Default;
        settings.TextZoom = 100;
        // settings.OffscreenPreRaster = true;
#pragma warning disable CS0618
        settings.EnableSmoothTransition();
#pragma warning restore CS0618

        // AndroidJSInterface methods will be available for invocation in js via 'window.Android' object.
        webView.AddJavascriptInterface(new AndroidJSInterface(webView), "Android");

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

using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platforms.MacOS.Handlers;
using Microsoft.Maui.Platforms.MacOS.Platform;
using WebKit;

namespace ActualChat.App.Maui;

/// <summary>
/// AppKit counterpart of <see cref="CustomBlazorWebViewHandler"/>: swaps the MAUI context to the
/// Blazor app's services, and rebuilds the WKWebView the labs handler creates, because extra URL
/// scheme handlers (content://) are accepted only before the web view is constructed.
/// </summary>
public sealed class MacOSCustomBlazorWebViewHandler : BlazorWebViewHandler
{
    private ILogger Log => field ??= StaticLog.For(GetType());

    public override void SetMauiContext(IMauiContext mauiContext)
    {
        BlazorWebViewApp.EnsureStarted();

        // Same trade-off as in CustomBlazorWebViewHandler: MainPage attaches the WebView only
        // once the app is ready, so this normally doesn't wait at all.
        if (!BlazorWebViewApp.WhenAppReady.IsCompleted) {
            var startedAt = CpuTimestamp.Now;
            BlazorWebViewApp.WhenAppReady.Wait();
            Log.LogWarning("Awaiting BlazorWebViewApp readiness blocked the UI thread for {Elapsed}",
                (CpuTimestamp.Now - startedAt).ToShortString());
        }

        var services = BlazorWebViewApp.Current.Services;
        base.SetMauiContext(new MacOSMauiContext(services));
    }

    protected override WKWebView CreatePlatformView()
    {
        // TODO(maui-labs): fold back into MauiWebView.MaciOS.OnInitializing once the labs handler
        // raises BlazorWebViewInitializing with its configuration.
        // Mirrors the base implementation - which is built entirely from private members - via
        // LabsBlazorWebViewHandlerExt, and adds the content:// handler the base can't take.
        var config = new WKWebViewConfiguration();
        // JS -> .NET half of the Blazor Hybrid transport: blazor.webview.js posts every message
        // to webkit.messageHandlers.webwindowinterop - the name is a fixed Blazor contract.
        config.UserContentController.AddScriptMessageHandler(this.NewWebViewScriptMessageHandler(), "webwindowinterop");
        // The window.external shim blazor.webview.js talks through (both directions), plus the
        // Blazor.start() call itself: index.html loads Blazor with autostart="false" and waits for it.
        config.UserContentController.AddUserScript(new WKUserScript(
            new NSString(LabsBlazorWebViewHandlerExt.BlazorInitScript), WKUserScriptInjectionTime.AtDocumentEnd, true));
        // The one MAUI host on the JS audio pipeline - see BrowserInfo.useWebAudio
        config.UserContentController.AddUserScript(new WKUserScript(
            new NSString("globalThis.__useWebAudio = true;"), WKUserScriptInjectionTime.AtDocumentStart, true));
        // Serves the app's own origin, app://0.0.0.1/ - index.html, _framework, _content - from the
        // bundle's wwwroot; nothing else answers that origin, there is no HTTP server behind the WebView.
        config.SetUrlSchemeHandler(this.NewAppSchemeHandler(), "app");
        // content://files/<key> previews of local files (attachments, gallery thumbnails) - what
        // MauiWebView.MaciOS registers from BlazorWebViewInitializing on iOS and Catalyst.
        config.SetUrlSchemeHandler(ContentSchemeHandler.Instance, "content");
        config.Preferences.JavaScriptCanOpenWindowsAutomatically = true;
        config.UpgradeKnownHostsToHttps = true;
        // Same as the iOS/Catalyst WebView: without this WebKit's autoplay policy keeps the
        // AudioContext silent until a user gesture it recognizes, so voice playback produces
        // no sound even though the pipeline runs.
        config.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;

        var webView = new LayoutInvalidatingWKWebView(CGRect.Empty, config);
        config.Preferences.SetValueForKey(NSObject.FromObject(true), new NSString("developerExtrasEnabled"));
        webView.SetValueForKey(NSObject.FromObject(false), new NSString("drawsBackground"));
        if (OperatingSystem.IsMacOSVersionAtLeast(13, 3))
            webView.Inspectable = true;
        return webView;
    }

    protected override void ConnectHandler(WKWebView platformView)
    {
        MauiWebView.Current?.SetPlatformWebView(platformView);
        base.ConnectHandler(platformView);
    }

    // Nested types

    private sealed class LayoutInvalidatingWKWebView(CGRect frame, WKWebViewConfiguration configuration)
        : WKWebView(frame, configuration)
    {
        public override void ViewDidMoveToSuperview()
        {
            base.ViewDidMoveToSuperview();

            // The labs ContentPageHandler adds page content without invalidating layout, so a
            // WebView attached after the first layout pass keeps a zero frame forever - our
            // MainPage attaches it only once BlazorWebViewApp is ready, long past that pass.
            // TODO(maui-labs): delete this subclass once the labs ContentPageHandler invalidates layout.
            if (Superview is { } superview)
                superview.NeedsLayout = true;
        }
    }
}

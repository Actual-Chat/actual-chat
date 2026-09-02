using System.Reflection;
using System.Globalization;
using ActualChat.Module;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using UIKit;
using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    // ReSharper disable once InconsistentNaming
    public WKWebView WKWebView { get; private set; } = null!;

    public partial void SetPlatformWebView(object platformWebView)
    {
        // Background & opacity live in CustomBlazorWebViewHandler's mapper - MAUI's own mappers run after this.
        if (ReferenceEquals(PlatformWebView, platformWebView))
            return;

        PlatformWebView = platformWebView;
        var webView = (WKWebView)platformWebView;
        WKWebView = webView;
        webView.ScrollView.Bounces = false;
        webView.ScrollView.ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never;
        webView.AllowsBackForwardNavigationGestures = false;

        // Prevent native scrolling so DOM can handle resizing via interactive-widget=resizes-content
        webView.ScrollView.ShowsVerticalScrollIndicator = false;
        webView.ScrollView.ShowsHorizontalScrollIndicator = false;
        webView.ScrollView.Scrolled += (sender, e) => {
            if (webView.ScrollView.ContentOffset.X != 0 || webView.ScrollView.ContentOffset.Y != 0) {
                webView.ScrollView.ContentOffset = new CoreGraphics.CGPoint(0, 0);
            }
        };
    }

    public partial void HardNavigateTo(string url)
    {
#pragma warning disable CA2000 // Call System.IDisposable.Dispose on object created by NSXxx
        var nsUrl = new NSUrl(url, false);
        var nsUrlRequest = new NSUrlRequest(nsUrl, NSUrlRequestCachePolicy.ReloadRevalidatingCacheData, 30);
        WKWebView.LoadRequest(nsUrlRequest);
#pragma warning restore CA2000
    }

    private partial Task EvaluateJSInternal(string code)
        => WKWebView.EvaluateJavaScriptAsync(code);

    // Private methods

    private partial void OnInitializing(object? sender, BlazorWebViewInitializingEventArgs eventArgs)
    {
        eventArgs.Configuration.AllowsInlineMediaPlayback = true;
        eventArgs.Configuration.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;
        eventArgs.Configuration.UpgradeKnownHostsToHttps = true;
        eventArgs.Configuration.Preferences.JavaScriptCanOpenWindowsAutomatically = true;

        // Allow loading images and media from the 'content' scheme (content://files/<path>).
        // Mirrors Windows behavior where local file previews are served via a custom content resolver.
        eventArgs.Configuration.SetUrlSchemeHandler(ContentSchemeHandler.Instance, "content");
    }

    private partial void OnInitialized(object? sender, BlazorWebViewInitializedEventArgs eventArgs)
    {
        var webView = eventArgs.WebView;
        SetPlatformWebView(webView);
        if (OperatingSystem.IsIOSVersionAtLeast(16, 4))
            webView.Inspectable = true;
    }

    private partial void OnLoaded(object? sender, EventArgs eventArgs)
        => WKWebView.UIDelegate = UIDelegate.Instance;

    private partial Task SetupCookies(Session session)
    {
        // Session cookie is not set here on iOS — we use a SessionTokens workaround instead.
        // See SessionTokens.AutoRefresh and api.getSessionToken() usages in TypeScript.
        // GCLB cookie is set so WebView WebSocket upgrades land on the same backend as the native HTTP/WS layer.
        var cookieStore = WKWebView.Configuration.WebsiteDataStore.HttpCookieStore;
        return SetCookie("GCLB", $"\"{AppLoadBalancerSettings.Instance.RouteId}\"");

        Task SetCookie(string name, string value) {
            var properties = new NSDictionary(
                NSHttpCookie.KeyName, name,
                NSHttpCookie.KeyValue, value,
                NSHttpCookie.KeyPath, "/",
                NSHttpCookie.KeyDomain, MauiSettings.Host,
                NSHttpCookie.KeySameSitePolicy, "none",
                NSHttpCookie.KeyVersion, "1", // Version 1 supports same site = none
                NSHttpCookie.KeySecure, new NSString("1"),
                NSHttpCookie.KeyExpires, NSDate.FromTimeIntervalSinceNow(60 * 60 * 24 * 7));
            var whenSetSource = TaskCompletionSourceExt.New();
            cookieStore.SetCookie(new NSHttpCookie(properties), () => whenSetSource.SetResult());
            return whenSetSource.Task;
        }
    }

    // Nested types

    private sealed class UIDelegate : WKUIDelegate
    {
        public static readonly UIDelegate Instance = new();

        public override void RequestMediaCapturePermission(
            WKWebView webView,
            WKSecurityOrigin origin,
            WKFrameInfo frame,
            WKMediaCaptureType type,
            Action<WKPermissionDecision> decisionHandler)
        {
            if (!IsAppOrigin(webView, origin)) {
                decisionHandler.Invoke(WKPermissionDecision.Deny);
                return;
            }

            if (IsMediaCaptureGranted(type)) {
                decisionHandler.Invoke(WKPermissionDecision.Grant);
                return;
            }

            _ = DispatchToBlazor(
                async c => {
                    var result = WKPermissionDecision.Prompt;
                    try {
                        var granted = type switch {
                            WKMediaCaptureType.Camera =>
                                await c.GetRequiredService<CameraPermissionHandler>()
                                    .CheckOrRequest().ConfigureAwait(true),
                            WKMediaCaptureType.Microphone =>
                                await c.GetRequiredService<MicrophonePermissionHandler>()
                                    .CheckOrRequest().ConfigureAwait(true),
                            WKMediaCaptureType.CameraAndMicrophone =>
                                await c.GetRequiredService<CameraPermissionHandler>()
                                    .CheckOrRequest().ConfigureAwait(true)
                                && await c.GetRequiredService<MicrophonePermissionHandler>()
                                    .CheckOrRequest().ConfigureAwait(true),
                            _ => false,
                        };
                        if (granted)
                            result = WKPermissionDecision.Grant;
                    }
                    catch {
                        // Intended
                    }
                    decisionHandler.Invoke(result);
                },
                "RequestMediaCapturePermission");
        }

        private static bool IsAppOrigin(WKWebView webView, WKSecurityOrigin origin)
        {
            // MAUI BlazorWebView serves the host page at app://0.0.0.1/ (MauiSettings.LocalHost),
            // and WebKit reports an empty host for opaque origins — which a custom-scheme page of
            // ours may be — so that case is accepted only while the WebView is on our local URI.
            var host = origin.Host;
            if (!host.IsNullOrEmpty())
                return host == MauiSettings.LocalHost;

            return IsCurrent(webView, out var mauiWebView) && mauiWebView.IsOnLocalUri;
        }

        private static bool IsMediaCaptureGranted(WKMediaCaptureType type)
        {
            return type switch {
                WKMediaCaptureType.Camera => IsGranted(AVAuthorizationMediaType.Video),
                WKMediaCaptureType.Microphone => IsGranted(AVAuthorizationMediaType.Audio),
                WKMediaCaptureType.CameraAndMicrophone => IsGranted(AVAuthorizationMediaType.Audio)
                    && IsGranted(AVAuthorizationMediaType.Video),
                _ => false,
            };

            bool IsGranted(AVAuthorizationMediaType type1)
                => AVCaptureDevice.GetAuthorizationStatus(type1) == AVAuthorizationStatus.Authorized;
        }
    }
}

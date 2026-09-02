using ActualChat.App.Maui.Services;
using ActualChat.Module;
using AVFoundation;
using Foundation;
using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    // ReSharper disable once InconsistentNaming
    public WKWebView WKWebView { get; private set; } = null!;

    public partial void SetPlatformWebView(object platformWebView)
    {
        if (ReferenceEquals(PlatformWebView, platformWebView))
            return;

        PlatformWebView = platformWebView;
        var webView = (WKWebView)platformWebView;
        WKWebView = webView;
        webView.AllowsBackForwardNavigationGestures = false;
        // The labs handler wires no delegates, so both are attached here: navigation policy
        // (external links, host->local rerouting) and media capture permissions.
        webView.NavigationDelegate = NavigationDelegate.Instance;
        webView.UIDelegate = UIDelegate.Instance;
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

    private partial Task SetupCookies(Session session)
    {
        // Session cookie is not set here — like on iOS, the SessionTokens workaround covers auth.
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

    private static bool HandleWebNavigation(MauiWebView? mauiWebView, Uri uri)
    {
        // AppKit twin of HandleLoading: MAUI's UrlLoading event doesn't exist on the labs
        // BlazorWebView, so the same policy runs from a WKNavigationDelegate instead. Static
        // and null-tolerant on purpose: an external navigation must be cancelled even when the
        // delegate can't match the current MauiWebView - failing open here is how an in-chat
        // link once replaced the whole app with the page it pointed at.
        if (uri.Host == MauiSettings.LocalHost) {
            if (mauiWebView != null) {
                mauiWebView.LastUri = uri;
                mauiWebView.LastLocalUri = uri;
            }
            return true;
        }

        if (!MauiSettings.BaseUri.IsBaseOf(uri)) {
            if (AllowedExternalHosts.Contains(uri.Host)) {
                if (mauiWebView != null)
                    mauiWebView.LastUri = uri;
                return true;
            }

            _ = ForegroundTask.Run(() => MauiBrowser.Open(uri.ToString()));
            return false;
        }

        // If we're here, it's a host URL, so we have to re-route it to the local one
        if (mauiWebView != null) {
            var wasOnLocalUri = mauiWebView.IsOnLocalUri;
            var localUri = HostToAbsoluteLocalUri(uri);
            BeginDispatchToMainThread(
                () => _ = mauiWebView.NavigateTo(localUri, !wasOnLocalUri),
                allowInline: false);
        }
        return false;
    }

    // Nested types

    private sealed class NavigationDelegate : WKNavigationDelegate
    {
        public static readonly NavigationDelegate Instance = new();

        public override void DecidePolicy(
            WKWebView webView,
            WKNavigationAction navigationAction,
            Action<WKNavigationActionPolicy> decisionHandler)
        {
            var url = navigationAction.Request?.Url?.AbsoluteString;
            Log.LogDebug("DecidePolicy: {Url}, type={Type}, isMainFrame={IsMainFrame}",
                url, navigationAction.NavigationType, navigationAction.TargetFrame?.MainFrame ?? true);
            if (url.IsNullOrEmpty()
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")) {
                // app:, about:, data:, blob: - WebKit-internal navigations
                decisionHandler.Invoke(WKNavigationActionPolicy.Allow);
                return;
            }

            IsCurrent(webView, out var mauiWebView);
            decisionHandler.Invoke(HandleWebNavigation(mauiWebView, uri)
                ? WKNavigationActionPolicy.Allow
                : WKNavigationActionPolicy.Cancel);
        }
    }

    private sealed class UIDelegate : WKUIDelegate
    {
        public static readonly UIDelegate Instance = new();

        public override WKWebView? CreateWebView(
            WKWebView webView,
            WKWebViewConfiguration configuration,
            WKNavigationAction navigationAction,
            WKWindowFeatures windowFeatures)
        {
            // window.open / target=_blank: no in-app popups - route through the same policy
            // (external URL -> default browser, host URL -> reroute into the app).
            var url = navigationAction.Request?.Url?.AbsoluteString;
            if (!url.IsNullOrEmpty()
                && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https") {
                IsCurrent(webView, out var mauiWebView);
                _ = HandleWebNavigation(mauiWebView, uri);
            }
            return null;
        }

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

            // Unlike iOS, there are no in-app permission handlers wired up here yet, so WebKit
            // drives the TCC prompt itself; once authorized, later calls grant silently.
            decisionHandler.Invoke(IsMediaCaptureGranted(type)
                ? WKPermissionDecision.Grant
                : WKPermissionDecision.Prompt);
        }

        private static bool IsAppOrigin(WKWebView webView, WKSecurityOrigin origin)
        {
            // The labs BlazorWebView serves the host page at app://0.0.0.1/ (MauiSettings.LocalHost),
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

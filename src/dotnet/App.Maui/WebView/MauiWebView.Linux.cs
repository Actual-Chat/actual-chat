using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    public WebKit.WebView WebKitWebView { get; private set; } = null!;

    public partial void SetPlatformWebView(object platformWebView)
    {
        if (ReferenceEquals(PlatformWebView, platformWebView))
            return;

        PlatformWebView = platformWebView;
        var webView = (WebKit.WebView)platformWebView;
        WebKitWebView = webView;
        // TODO(maui-labs): everything below stands in for the BlazorWebViewInitializing and
        // UrlLoading events - see MauiWebView.cs.
        var settings = webView.GetSettings();
        settings.EnableDeveloperExtras = MauiSettings.AreDevToolsEnabled;
#if DEBUG
        // The page's console goes to the app's stdout, next to the app log
        settings.EnableWriteConsoleMessagesToStdout = true;
#endif
        // Off by default in WebKitGTK; calls and video need both
        settings.EnableMediaStream = true;
        settings.EnableWebrtc = true;
        // Same as in the other WebKit WebViews: without this the autoplay policy keeps the
        // AudioContext silent until a user gesture it recognizes
        settings.MediaPlaybackRequiresUserGesture = false;
        settings.JavascriptCanOpenWindowsAutomatically = true;
        // One more MAUI host on the JS audio pipeline - see BrowserInfo.useWebAudio
        var useWebAudioScript = UserScript.New(
            "globalThis.__useWebAudio = true;",
            UserContentInjectedFrames.AllFrames,
            UserScriptInjectionTime.Start,
            null,
            null);
        webView.GetUserContentManager().AddScript(useWebAudioScript);
        webView.OnDecidePolicy += OnDecidePolicy;
        webView.OnCreate += OnCreate;
        webView.OnPermissionRequest += OnPermissionRequest;
        webView.OnWebProcessTerminated += OnWebProcessTerminated;
    }

    public partial void HardNavigateTo(string url)
        => WebKitWebView.LoadUri(url);

    private partial Task EvaluateJSInternal(string code)
        => WebKitWebView.EvaluateJavascriptAsync(code);

    // Private methods

    private partial Task SetupCookies(Session session)
        // Session cookie is not set here - like in the Apple WebViews, the SessionTokens workaround
        // covers auth. See SessionTokens.AutoRefresh and api.getSessionToken() usages in TypeScript.
        // TODO(FC): set the GCLB cookie the other platforms set, so WebView WebSocket upgrades land
        // on the same backend as the native HTTP/WS layer - GirCore 0.7 has no binding for
        // webkit_cookie_manager_add_cookie.
        => Task.CompletedTask;

    private static bool OnDecidePolicy(WebKit.WebView webView, WebKit.WebView.DecidePolicySignalArgs args)
    {
        // False leaves the decision to WebKit, i.e. the navigation proceeds
        if (args.Decision is not NavigationPolicyDecision decision)
            return false;

        var url = decision.GetNavigationAction().GetRequest().GetUri();
        Log.LogDebug("DecidePolicy: {Url}, type={Type}", url, args.DecisionType);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false; // app:, about:, data:, blob: - WebKit-internal navigations

        // target=_blank: no in-app windows, the link goes through the policy and nowhere else
        var isNewWindow = args.DecisionType == PolicyDecisionType.NewWindowAction;
        IsCurrent(webView, out var mauiWebView);
        if (HandleWebNavigation(mauiWebView, uri) && !isNewWindow)
            return false;

        decision.Ignore();
        return true;
    }

    private static Gtk.Widget OnCreate(WebKit.WebView webView, WebKit.WebView.CreateSignalArgs args)
    {
        // window.open: the same as target=_blank above; no widget means no window
        var url = args.NavigationAction.GetRequest().GetUri();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") {
            IsCurrent(webView, out var mauiWebView);
            _ = HandleWebNavigation(mauiWebView, uri);
        }
        return null!;
    }

    private static bool OnPermissionRequest(
        WebKit.WebView webView,
        WebKit.WebView.PermissionRequestSignalArgs args)
    {
        // No system prompt stands behind these on a Linux desktop - WebKitGTK leaves the answer to
        // the embedder and denies by default. So the app's own page gets what the app uses, and
        // nothing else gets anything.
        var request = args.Request;
        var isAppRequest = request
            is UserMediaPermissionRequest
            or DeviceInfoPermissionRequest
            or GeolocationPermissionRequest
            or ClipboardPermissionRequest;
        if (isAppRequest && IsCurrent(webView, out var mauiWebView) && mauiWebView.IsOnLocalUri)
            request.Allow();
        else
            request.Deny();
        return true;
    }

    private static void OnWebProcessTerminated(
        WebKit.WebView webView,
        WebKit.WebView.WebProcessTerminatedSignalArgs args)
    {
        // A crashed web process leaves a blank window that ignores input, so the view is replaced.
        // TerminatedByApi is LinuxCustomBlazorWebViewHandler dropping a view it disconnects.
        var reason = args.Reason;
        Log.LogWarning("Web process terminated: {Reason}", reason);
        if (reason == WebProcessTerminationReason.TerminatedByApi || !IsCurrent(webView, out var mauiWebView))
            return;

        BeginDispatchToMainThread(() => {
            if (Current == mauiWebView)
                MainPage.Current.RecreateWebView();
        }, allowInline: false);
    }
}

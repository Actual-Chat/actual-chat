using ActualChat.Permissions;
using AVFoundation;
using Foundation;
using Microsoft.AspNetCore.Components.WebView;
using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    // ReSharper disable once InconsistentNaming
    public WKWebView WKWebView { get; } = (WKWebView)platformWebView;

    public partial void OnHandlerConnected()
    {
        WKWebView.ScrollView.Bounces = false;
        WKWebView.AllowsBackForwardNavigationGestures = false;
    }

    public partial void OnHandlerDisconnected() { }

    public partial void OnInitializing(BlazorWebViewInitializingEventArgs eventArgs)
    {
        eventArgs.Configuration.AllowsInlineMediaPlayback = true;
        eventArgs.Configuration.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;
        eventArgs.Configuration.UpgradeKnownHostsToHttps = true;
        eventArgs.Configuration.Preferences.JavaScriptCanOpenWindowsAutomatically = true;
    }

    public partial void OnInitialized(BlazorWebViewInitializedEventArgs eventArgs)
    {
        if (DeviceInfo.Version >= new Version("16.4"))
            WKWebView.Inspectable = true;
    }

    public partial void OnLoaded(EventArgs eventArgs)
        => WKWebView.UIDelegate = UIDelegate.Instance;

    public partial Task EvaluateJavaScript(string javaScript)
    {
        var tcs = new TaskCompletionSource<NSObject>();
        WKWebView.EvaluateJavaScript(javaScript, (result, error) => {
            var e = error.ToException();
            if (e != null)
                tcs.TrySetException(e);
            else
                tcs.TrySetResult(result);
        });
        return tcs.Task;
    }

    // Private methods

    private partial void SetupSessionCookie(Session session)
    {
        SetupDomainCookie(WKWebView, MauiSettings.LocalHost, session, false);
        SetupDomainCookie(WKWebView, MauiSettings.Host, session, true);

        static void SetupDomainCookie(WKWebView webView, string domain, Session session, bool isSecure)
        {
            var cookieName = Constants.Session.CookieName;
            var sessionId = session.Id.Value;
            var properties = isSecure
                ? new NSDictionary(
                    NSHttpCookie.KeyName, cookieName,
                    NSHttpCookie.KeyValue, sessionId,
                    NSHttpCookie.KeyPath, "/",
                    NSHttpCookie.KeyDomain, domain,
                    NSHttpCookie.KeySameSitePolicy, "none",
                    NSHttpCookie.KeyVersion, "1") // Version 1 supports same site = none
                : new NSDictionary(
                    NSHttpCookie.KeyName, cookieName,
                    NSHttpCookie.KeyValue, sessionId,
                    NSHttpCookie.KeyPath, "/",
                    NSHttpCookie.KeyDomain, domain,
                    NSHttpCookie.KeySameSitePolicy, "none",
                    NSHttpCookie.KeyVersion, "1", // Version 1 supports same site = none
                    NSHttpCookie.KeySecure,  new NSString ("1"),
                    NSHttpCookie.KeyExpires, NSDate.FromTimeIntervalSinceNow(60*60*24*7)
                );
            webView.Configuration.WebsiteDataStore.HttpCookieStore.SetCookie(new NSHttpCookie(properties), null);
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
            if (IsMediaCaptureGranted(origin, type)) {
                decisionHandler(WKPermissionDecision.Grant);
                return;
            }

            _ = DispatchToBlazor(
                c => {
                    var permissionHandler = c.GetRequiredService<MicrophonePermissionHandler>();
                    var resultValueTask = permissionHandler.CheckOrRequest();
                    if (resultValueTask.IsCompleted) {
                        var result = resultValueTask.Result
                            ? WKPermissionDecision.Grant
                            : WKPermissionDecision.Prompt;
                        decisionHandler.Invoke(result);
                    }
                    else
                        _ = resultValueTask
                            .AsTask()
                            .ContinueWith(t => {
                                var result = t is { IsCompletedSuccessfully: true, Result: true }
                                    ? WKPermissionDecision.Grant
                                    : WKPermissionDecision.Prompt;
                                decisionHandler.Invoke(result);
                            }, TaskScheduler.Default);
                },
                "RequestMediaCapturePermission");
        }

        private bool IsMediaCaptureGranted(
            WKSecurityOrigin origin,
            WKMediaCaptureType type)
        {
            if (!origin.Host.IsNullOrEmpty())
                return false;

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

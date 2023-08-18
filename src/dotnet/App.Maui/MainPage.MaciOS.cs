using AVFoundation;
using Foundation;
using Microsoft.AspNetCore.Components.WebView;
using WebKit;

namespace ActualChat.App.Maui;

public partial class MainPage
{
    /// <summary>
    /// Gets the <see cref="WKWebView"/> instance that was initialized.
    /// the default values to allow further configuring additional options.
    /// </summary>
    public WKWebView? PlatformWebView { get; private set; }

    public partial void SetupSessionCookie(Session session)
    {
        SetupSessionCookie(MauiSettings.LocalHost, session, false);
        SetupSessionCookie(MauiSettings.Host, session, true);
    }

    private void SetupSessionCookie(string domain, Session session, bool isSecure)
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
        PlatformWebView!.Configuration.WebsiteDataStore.HttpCookieStore.SetCookie(new NSHttpCookie(properties), null);
    }

    // To manage iOS permissions, update Info.plist to include the necessary keys to access
    // the APIs required by your app. You may have to perform additional configuration to enable
    // use of those APIs from the WebView, as is done below.

    private partial void OnWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    {
        e.Configuration.AllowsInlineMediaPlayback = true;
        e.Configuration.MediaTypesRequiringUserActionForPlayback = WebKit.WKAudiovisualMediaTypes.None;
        e.Configuration.UpgradeKnownHostsToHttps = true;
    }

    private partial void OnWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        var webView = PlatformWebView = e.WebView;
        if (DeviceInfo.Version >= new Version("16.4"))
            webView.Inspectable = true;
    }

    private partial void OnWebViewLoaded(object? sender, EventArgs e)
        => PlatformWebView!.UIDelegate = new UIDelegate();

    private sealed class UIDelegate : WKUIDelegate
    {
        public override void RequestMediaCapturePermission(WKWebView webView, WKSecurityOrigin origin, WKFrameInfo frame, WKMediaCaptureType type, Action<WKPermissionDecision> decisionHandler)
        {
            if (IsMediaCaptureGranted(origin, type)) {
                decisionHandler(WKPermissionDecision.Grant);
                return;
            }

            base.RequestMediaCapturePermission(webView, origin, frame, type, decisionHandler);
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

            bool IsGranted(AVAuthorizationMediaType type)
                => AVCaptureDevice.GetAuthorizationStatus(type) == AVAuthorizationStatus.Authorized;
        }
    }
}

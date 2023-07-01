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

    public partial void SetupSessionCookie(Uri baseUri, Session session)
    {
        SetSessionCookie(AppHostAddress, session, false);
        SetSessionCookie(baseUri.Host, session, true);
    }

    private void SetSessionCookie(string domain, Session session, bool isSecure)
    {
        var properties = isSecure
            ? new NSDictionary(
                NSHttpCookie.KeyName, "FusionAuth.SessionId",
                NSHttpCookie.KeyValue, session.Id.Value,
                NSHttpCookie.KeyPath, "/",
                NSHttpCookie.KeyDomain, domain,
                NSHttpCookie.KeySameSitePolicy, "none",
                NSHttpCookie.KeyVersion, "1")
            : new NSDictionary(
                NSHttpCookie.KeyName, "FusionAuth.SessionId",
                NSHttpCookie.KeyValue, session.Id.Value,
                NSHttpCookie.KeyPath, "/",
                NSHttpCookie.KeyDomain, domain,
                NSHttpCookie.KeySameSitePolicy, "none",
                NSHttpCookie.KeyVersion, "1", // version 1 supports same site none
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
        var webView = e.WebView;
        webView.Inspectable = true;
        PlatformWebView = webView;
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

using AVFoundation;
using Foundation;
using Microsoft.AspNetCore.Components.WebView.Maui;
using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WKWebView platformView)
    {
        Log.LogDebug("MauiBlazorWebViewHandler.ConnectHandler");
        base.ConnectHandler(platformView);

        SetSessionIdCookie(UrlMapper.BaseUri.Host, true);
        SetSessionIdCookie("0.0.0.0", false);
        InjectInitPageScript();
    }

    void SetSessionIdCookie(string domain, bool secure)
    {
        var properties = new NSDictionary(
            NSHttpCookie.KeyName, "FusionAuth.SessionId",
            NSHttpCookie.KeyValue, AppSettings.SessionId,
            NSHttpCookie.KeyPath, "/",
            NSHttpCookie.KeyDomain, domain,
            NSHttpCookie.KeySameSitePolicy, "none");
        // if (secure)
        //     properties[NSHttpCookie.KeySecure] = NSObject.FromObject("TRUE");
        PlatformView.Configuration.WebsiteDataStore.HttpCookieStore.SetCookie(new NSHttpCookie(properties), null);
    }

    private void InjectInitPageScript()
    {
        var baseUri = UrlMapper.BaseUri;
        var sessionId = AppSettings.SessionId;
        var initScript = $"window.App.initPage('{baseUri}', '{sessionId}')";
        var addLoadHandlerScript = $"document.addEventListener('DOMContentLoaded', e => {{ {initScript} }});";
        // PlatformView.EvaluateJavaScript(addLoadHandlerScript, (result, error) => { });
        PlatformView.Configuration.UserContentController.AddUserScript(
            new WKUserScript(
                new NSString(addLoadHandlerScript),
                WKUserScriptInjectionTime.AtDocumentStart,
                true));
    }
}

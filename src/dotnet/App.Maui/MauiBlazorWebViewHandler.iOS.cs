using Foundation;
using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WKWebView platformView)
    {
        Log.LogDebug("MauiBlazorWebViewHandler.ConnectHandler");
        base.ConnectHandler(platformView);

        var baseUri = UrlMapper.BaseUri;
        var sessionId = AppSettings.SessionId;
        SetSessionIdCookie(baseUri.Host, true);
        SetSessionIdCookie("0.0.0.0", false);

        // init page
        var initScript = $"window.App.initPage('{baseUri}', '{sessionId}')";
        var addLoadHandlerScript = $"document.addEventListener('DOMContentLoaded', e => {{ {initScript} }});";
        // PlatformView.EvaluateJavaScript(addLoadHandlerScript, (result, error) => { });
        PlatformView.Configuration.UserContentController.AddUserScript(
            new WKUserScript(
                new NSString(addLoadHandlerScript),
                WKUserScriptInjectionTime.AtDocumentStart,
                true));

        void SetSessionIdCookie(string domain, bool secure)
        {
            var properties = new NSDictionary(
                NSHttpCookie.KeyName, "FusionAuth.SessionId",
                NSHttpCookie.KeyValue, sessionId,
                NSHttpCookie.KeyPath, "/",
                NSHttpCookie.KeyDomain, domain,
                NSHttpCookie.KeySameSitePolicy, "none");
            // if (secure)
            //     properties[NSHttpCookie.KeySecure] = NSObject.FromObject("TRUE");
            platformView.Configuration.WebsiteDataStore.HttpCookieStore.SetCookie(new NSHttpCookie(properties), null);
        }
    }
}

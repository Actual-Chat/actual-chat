using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WKWebView platformView)
    {
        Log.LogDebug("MauiBlazorWebViewHandler.ConnectHandler");
        base.ConnectHandler(platformView);

        PlatformView.ScrollView.Bounces = false;
        PlatformView.AllowsBackForwardNavigationGestures = false;
    }
}

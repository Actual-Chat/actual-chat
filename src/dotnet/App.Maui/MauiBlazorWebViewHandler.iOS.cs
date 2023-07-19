using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WKWebView platformView)
    {
        Tracer.Point(nameof(ConnectHandler));
        base.ConnectHandler(platformView);
        PlatformView.ScrollView.Bounces = false;
        PlatformView.AllowsBackForwardNavigationGestures = false;
    }
}

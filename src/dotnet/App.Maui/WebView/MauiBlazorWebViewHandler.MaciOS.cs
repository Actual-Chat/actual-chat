using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WKWebView platformView)
    {
        MauiWebView.Current?.SetPlatformWebView(platformView);
        base.ConnectHandler(platformView);
    }
}

using WebKit;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WKWebView platformView)
    {
        base.ConnectHandler(platformView);
        MauiWebView.Current?.SetPlatformWebView(platformView);
    }
}

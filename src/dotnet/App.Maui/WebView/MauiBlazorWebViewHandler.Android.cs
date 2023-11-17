using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WebView platformView)
    {
        MauiWebView.Current?.SetPlatformWebView(platformView);
        base.ConnectHandler(platformView);
    }
}

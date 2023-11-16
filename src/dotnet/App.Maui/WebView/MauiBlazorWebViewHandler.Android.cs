using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WebView platformView)
    {
        base.ConnectHandler(platformView);
        MauiWebView.Current?.SetPlatformWebView(platformView);
    }
}

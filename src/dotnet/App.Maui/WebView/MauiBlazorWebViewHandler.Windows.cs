using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WebView2Control platformView)
    {
        MauiWebView.Current?.SetPlatformWebView(platformView);
        base.ConnectHandler(platformView);
    }
}

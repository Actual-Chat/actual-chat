using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler
{
    protected override void ConnectHandler(WebView2Control platformView)
    {
        Log.LogDebug("MauiBlazorWebViewHandler.ConnectHandler");
        base.ConnectHandler(platformView);

        platformView.CoreWebView2Initialized += CoreWebView2Initialized;
    }

    protected override void DisconnectHandler(WebView2Control platformView)
    {
        platformView.CoreWebView2Initialized -= CoreWebView2Initialized;

        base.DisconnectHandler(platformView);
    }

    private void CoreWebView2Initialized(WebView2Control sender, Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs args)
    {
        var ctrl = sender.CoreWebView2;


        ctrl.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;

namespace ActualChat.ClientApp;

partial class MauiBlazorWebViewHandler
{
    protected override Android.Webkit.WebView CreatePlatformView()
    {
        var settings = MauiContext!.Services.GetRequiredService<ClientAppSettings>();
        var webView = base.CreatePlatformView();
        return webView;
    }
}

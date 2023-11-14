using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public static class BlazorWebViewHandlerExt
{
    public static WebViewManager? GetWebViewManager(this BlazorWebViewHandler webViewHandler)
    {
        var field = webViewHandler.GetType().GetField("_webViewManager", BindingFlags.Instance | BindingFlags.NonPublic);
        return (WebViewManager?)field?.GetValue(webViewHandler);
    }
}

using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public static class BlazorWebViewHandlerExt
{
    [UnconditionalSuppressMessage("Trimming",
        "IL2075: Call argument does not satisfy 'DynamicallyAccessedMemberTypes.NonPublicFields' in call to 'System.Type.GetField...",
        Justification = "This field should be there for sure")]
    public static WebViewManager? GetWebViewManager(this BlazorWebViewHandler webViewHandler)
    {
        var field = webViewHandler.GetType().GetField("_webViewManager", BindingFlags.Instance | BindingFlags.NonPublic);
        return (WebViewManager?)field?.GetValue(webViewHandler);
    }
}

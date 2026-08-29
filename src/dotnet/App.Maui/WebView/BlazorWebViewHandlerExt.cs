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
        // Named _webviewManager on every platform, but typed per platform and internal - UnsafeAccessor can't bind it.
        var field = typeof(BlazorWebViewHandler)
            .GetField("_webviewManager", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw StandardError.Constraint("No '_webviewManager' field in BlazorWebViewHandler - MAUI renamed it?");

        return (WebViewManager?)field.GetValue(webViewHandler);
    }
}

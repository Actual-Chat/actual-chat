using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public static class BlazorWebViewAttachedProps
{
    public static readonly BindableProperty IsDisconnectedProperty =
        BindableProperty.Create("IsDisconnected", typeof(bool), typeof(BlazorWebView));

    public static bool GetIsDisconnected(this BlazorWebView webView)
        => (bool)webView.GetValue(IsDisconnectedProperty);

    public static void SetIsDisconnected(this BlazorWebView webView, bool isDisconnected)
        => webView.SetValue(IsDisconnectedProperty, isDisconnected);
}

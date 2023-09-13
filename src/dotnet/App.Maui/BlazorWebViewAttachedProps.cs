using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public static class BlazorWebViewAttachedProps
{
    public static readonly BindableProperty DisconnectMarkerProperty =
        BindableProperty.Create("DisconnectMarker", typeof(BlazorWebViewDisconnectMarker), typeof(BlazorWebView));

    public static BlazorWebViewDisconnectMarker? GetDisconnectMarker(this BlazorWebView webView)
        => (BlazorWebViewDisconnectMarker)webView.GetValue(DisconnectMarkerProperty);

    public static void SetDisconnectMarker(this BlazorWebView webView, BlazorWebViewDisconnectMarker? disconnectMarker)
        => webView.SetValue(DisconnectMarkerProperty, disconnectMarker);
}

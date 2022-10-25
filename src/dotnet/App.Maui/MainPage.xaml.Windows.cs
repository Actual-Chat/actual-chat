using Microsoft.AspNetCore.Components.WebView;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

public partial class MainPage
{
    public WebView2Control? PlatformWebView { get; private set; }

    private partial void BlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    { }

    private partial void BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        => PlatformWebView = e.WebView;
}

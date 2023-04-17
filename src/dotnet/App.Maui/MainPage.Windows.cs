using Microsoft.AspNetCore.Components.WebView;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace ActualChat.App.Maui;

public partial class MainPage
{
    public WebView2Control? PlatformWebView { get; private set; }

    private partial void OnBlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    { }

    private partial void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        => PlatformWebView = e.WebView;

    private partial void OnBlazorWebViewLoaded(object? sender, EventArgs e)
    { }
}

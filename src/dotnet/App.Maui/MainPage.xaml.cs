using Microsoft.AspNetCore.Components.WebView;

namespace ActualChat.App.Maui;

public partial class MainPage : ContentPage
{
    public MainPage(ClientAppSettings appSettings)
    {
        InitializeComponent();

        _blazorWebView.BlazorWebViewInitializing += BlazorWebViewInitializing;
        _blazorWebView.BlazorWebViewInitialized += BlazorWebViewInitialized;

        _blazorWebView.RootComponents.Add(
            new Microsoft.AspNetCore.Components.WebView.Maui.RootComponent {
                ComponentType = typeof(ActualChat.UI.Blazor.App.App),
                Selector = "#app",
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal) {
                    {
                        nameof(ActualChat.UI.Blazor.App.App.SessionId),
                        appSettings.SessionId
                    },
                }
            });
    }

    private partial void BlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e);
    private partial void BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e);
}

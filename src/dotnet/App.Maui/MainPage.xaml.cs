using Microsoft.AspNetCore.Components.WebView;
using ActualChat.App.Maui.Services;

namespace ActualChat.App.Maui;

public partial class MainPage : ContentPage
{
    private readonly ClientAppSettings _appSettings;
    private readonly NavigationInterceptor _navInterceptor;

    public MainPage(ClientAppSettings appSettings, NavigationInterceptor navInterceptor)
    {
        _appSettings = appSettings;
        _navInterceptor = navInterceptor;

        InitializeComponent();

        _blazorWebView.BlazorWebViewInitializing += BlazorWebViewInitializing;
        _blazorWebView.BlazorWebViewInitialized += BlazorWebViewInitialized;
        _blazorWebView.UrlLoading += OnUrlLoading;

        _blazorWebView.RootComponents.Add(
            new Microsoft.AspNetCore.Components.WebView.Maui.RootComponent {
                ComponentType = typeof(MauiBlazorApp),
                Selector = "#app",
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal) {
                    { nameof(MauiBlazorApp.SessionId), appSettings.SessionId },
                },
            });
    }

    private void OnUrlLoading(object? sender, UrlLoadingEventArgs eventArgs)
    {
        var uri = eventArgs.Url;
        if (_navInterceptor.TryIntercept(uri))
            // On Windows platform load cancellation seems not working while issues are closed a while ago. Uri is opened in WebView.
            // https://github.com/MicrosoftEdge/WebView2Feedback/issues/560
            // https://github.com/MicrosoftEdge/WebView2Feedback/issues/2072
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
    }

    private partial void BlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e);
    private partial void BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e);
}

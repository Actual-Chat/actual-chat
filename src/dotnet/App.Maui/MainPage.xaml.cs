using Microsoft.AspNetCore.Components.WebView;
using ActualChat.App.Maui.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public partial class MainPage : ContentPage
{
    private ClientAppSettings AppSettings { get; }
    private NavigationInterceptor NavInterceptor { get; }
    private ILogger Log { get; }

    public BlazorWebView BlazorWebView
        => this._blazorWebView;

    public MainPage(ClientAppSettings appSettings, NavigationInterceptor navInterceptor, ILogger<MainPage> log)
    {
        TraceSession.Main.Track("MainPage.Constructor");
        AppSettings = appSettings;
        NavInterceptor = navInterceptor;
        Log = log;

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
        TraceSession.Main.Track($"MainPage.OnUrlLoading. Url: '{uri}'");
        if (NavInterceptor.TryIntercept(uri))
            // On Windows platform load cancellation seems not working while issues are closed a while ago. Uri is opened in WebView.
            // https://github.com/MicrosoftEdge/WebView2Feedback/issues/560
            // https://github.com/MicrosoftEdge/WebView2Feedback/issues/2072
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
    }

    private partial void BlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e);
    private partial void BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e);
}

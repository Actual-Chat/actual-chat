using Microsoft.AspNetCore.Components.WebView;
using ActualChat.App.Maui.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public partial class MainPage : ContentPage
{
    private ClientAppSettings AppSettings { get; }
    private NavigationInterceptor NavInterceptor { get; }
    private ILogger Log { get; }
    private Tracer Tracer { get; } = Tracer.Default[nameof(MainPage)];

    public BlazorWebView BlazorWebView
        => _blazorWebView;

    public MainPage(ClientAppSettings appSettings, NavigationInterceptor navInterceptor, ILogger<MainPage> log)
    {
        Log = log;
        Tracer.Point(".ctor");
        AppSettings = appSettings;
        NavInterceptor = navInterceptor;

        InitializeComponent();
        _blazorWebView.BlazorWebViewInitializing += OnBlazorWebViewInitializing;
        _blazorWebView.BlazorWebViewInitialized += OnBlazorWebViewInitialized;
        _blazorWebView.UrlLoading += OnUrlLoading;
        _blazorWebView.Loaded += OnBlazorWebViewLoaded;

        _blazorWebView.RootComponents.Add(
            new Microsoft.AspNetCore.Components.WebView.Maui.RootComponent {
                ComponentType = typeof(MauiBlazorApp),
                Selector = "#app",
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal) {
                    { nameof(MauiBlazorApp.SessionId), appSettings.SessionId },
                },
            });
    }

    private partial void OnBlazorWebViewLoaded(object? sender, EventArgs e);

    private void OnUrlLoading(object? sender, UrlLoadingEventArgs eventArgs)
    {
        var uri = eventArgs.Url;
        Tracer.Point($"OnUrlLoading: Url: '{uri}'");
        if (NavInterceptor.TryIntercept(uri))
            // Load cancellation seems not working On Windows platform,
            // even though the issues were closed a while ago, and  Uri gets opened in WebView.
            // See:
            // - https://github.com/MicrosoftEdge/WebView2Feedback/issues/560
            // - https://github.com/MicrosoftEdge/WebView2Feedback/issues/2072
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
    }

    private partial void OnBlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e);
    private partial void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e);
}

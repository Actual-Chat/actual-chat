using Microsoft.AspNetCore.Components.WebView;
using ActualChat.App.Maui.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public partial class MainPage : ContentPage
{
    private readonly BlazorWebView _webView;
    private MauiNavigationInterceptor? _navigationInterceptor;

    public static MainPage? Current => Application.Current?.MainPage as MainPage;

    private Tracer Tracer { get; } = Tracer.Default[nameof(MainPage)];
    private MauiNavigationInterceptor NavigationInterceptor =>
        _navigationInterceptor ??= Services.GetRequiredService<MauiNavigationInterceptor>();

    public IServiceProvider Services { get; } // This is root IServiceProvider!

    public MainPage(IServiceProvider services)
    {
        Tracer.Point(".ctor");
        Services = services;
        BackgroundColor = Color.FromRgb(0x44, 0x44, 0x44);

        _webView = new BlazorWebView {
            HostPage = "wwwroot/index.html",
        };
        _webView.BlazorWebViewInitializing += OnWebViewInitializing;
        _webView.BlazorWebViewInitialized += OnWebViewInitialized;
        _webView.UrlLoading += OnWebViewUrlLoading;
        _webView.Loaded += OnWebViewLoaded;
        Content = _webView;

        _webView.RootComponents.Add(
            new RootComponent {
                ComponentType = typeof(MauiBlazorApp),
                Selector = "#app",
            });
    }

    public partial void SetupSessionCookie(Session session);

    public partial void NavigateTo(string url);

    // Private methods

    private partial void OnWebViewLoaded(object? sender, EventArgs e);

    private void OnWebViewUrlLoading(object? sender, UrlLoadingEventArgs eventArgs)
    {
        var uri = eventArgs.Url;
        NavigationInterceptor.TryIntercept(uri, eventArgs);
        Tracer.Point($"{nameof(OnWebViewUrlLoading)}: Url: '{uri}' -> {eventArgs.UrlLoadingStrategy}");
    }

    private partial void OnWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e);
    private partial void OnWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e);
}

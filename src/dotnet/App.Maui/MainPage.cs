using Microsoft.AspNetCore.Components.WebView;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Platform;

namespace ActualChat.App.Maui;

public partial class MainPage : ContentPage
{
    private readonly BlazorWebView _webView;

    private MauiNavigationInterceptor NavigationInterceptor { get; }
    private Tracer Tracer { get; } = Tracer.Default[nameof(MainPage)];

    public static MainPage? Current
        => Application.Current?.MainPage as MainPage;

    public MainPage(MauiNavigationInterceptor navigationInterceptor)
    {
        Tracer.Point(".ctor");
        NavigationInterceptor = navigationInterceptor;
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

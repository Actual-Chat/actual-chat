using Microsoft.AspNetCore.Components.WebView;
using ActualChat.App.Maui.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;
#if WINDOWS
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;
#elif ANDROID
using AWebView = Android.Webkit.WebView;
#elif IOS || MACCATALYST
using WebKit;
#endif

namespace ActualChat.App.Maui;

public partial class MainPage : ContentPage
{
    private readonly ClientAppSettings _appSettings;
    private readonly NavigationInterceptor _navInterceptor;

    public BlazorWebView BlazorWebView
        => this._blazorWebView;

#if WINDOWS
		/// <summary>
		/// Gets the <see cref="WebView2Control"/> instance that was initialized.
		/// </summary>
		public WebView2Control? PlatformWebView { get; private set; }
#elif ANDROID
		/// <summary>
		/// Gets the <see cref="AWebView"/> instance that was initialized.
		/// </summary>
		public AWebView? PlatformWebView { get; private set; }
#elif MACCATALYST || IOS
		/// <summary>
		/// Gets the <see cref="WKWebView"/> instance that was initialized.
		/// the default values to allow further configuring additional options.
		/// </summary>
		public WKWebView? PlatformWebView { get; private set; }
#endif

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

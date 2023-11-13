using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public class MainPage : ContentPage
{
    private static MainPage _current = null!;

    public static MainPage Current => _current;

    public BlazorWebView BlazorWebView { get; private set; } = null!;

    public MainPage()
    {
        BackgroundColor = Color.FromRgb(0x44, 0x44, 0x44);
        Interlocked.Exchange(ref _current, this);
        RecreateWebView();
    }

    public void RecreateWebView()
    {
        MauiWebView.Current?.Deactivate();
        var webView = BlazorWebView = new BlazorWebView {
            HostPage = "wwwroot/index.html",
        };
        webView.BlazorWebViewInitializing += (_, ea) => MauiWebView.IfActive(webView)?.OnInitializing(ea);
        webView.BlazorWebViewInitialized += (_, ea) => MauiWebView.IfActive(webView)?.OnInitialized(ea);
        webView.UrlLoading += (_, ea) => MauiWebView.IfActive(webView)?.OnUrlLoading(ea);
        webView.Loaded += (_, ea) => MauiWebView.IfActive(webView)?.OnLoaded(ea);
        webView.Unloaded += (sender, ea) => {
            // BlazorWebView.Handler.DisconnectHandler synchronously waits for DisposeAsync task completion,
            // which may cause a deadlock on the main thread. We workaround it by:
            // - Deactivating webView synchronously
            // - Starting to dispose Handler._webViewManager in the main thread
            // - Once it completes, we call DisconnectHandler, which shouldn't dispose anything at that point.
            //
            // See:
            // - https://github.com/dotnet/maui/blob/main/src/BlazorWebView/src/Maui/Windows/BlazorWebViewHandler.Windows.cs#L35
            // - https://github.com/dotnet/maui/blob/main/src/BlazorWebView/src/Maui/Android/BlazorWebViewHandler.Android.cs#L70
            // - https://github.com/dotnet/aspnetcore/blob/main/src/Components/WebView/WebView/src/WebViewManager.cs#L264
            // - https://github.com/dotnet/aspnetcore/blob/main/src/Components/WebView/WebView/src/PageContext.cs#L58
            MauiWebView.IfActive(webView)?.Deactivate();
            if (webView.Handler is not BlazorWebViewHandler handler)
                return;

            if (handler.GetWebViewManager() is { } webViewManager)
                _ = MainThread.InvokeOnMainThreadAsync(async () => {
                    await webViewManager.DisposeSilentlyAsync().ConfigureAwait(true);
                    webView.Handler?.DisconnectHandler();
                });
        };
        webView.RootComponents.Add(
            new RootComponent {
                ComponentType = typeof(MauiBlazorAppWrapper),
                Selector = "#app",
            });
        Content = webView;
    }
}

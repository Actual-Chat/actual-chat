namespace ActualChat.App.Maui;

public partial class MainPage
{
    private partial void OnLoaded_Platform()
    {
        // Safe areas handled by CSS via viewport-fit=cover and env(safe-area-inset-*)
    }

    private partial Task WhenPlatformWebViewReady()
        // Chromium's provider lock is what the BlazorWebView ctor would otherwise block the UI
        // thread on, so the WebView is attached only once this warm-up has taken it.
        => AndroidUtils.WarmUpWebView();
}

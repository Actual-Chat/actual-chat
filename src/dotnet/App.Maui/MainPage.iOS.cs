namespace ActualChat.App.Maui;

public partial class MainPage
{
    private partial void OnLoaded_Platform()
    {
        // .NET 10: ContentPage defaults to SafeAreaEdges.None (edge-to-edge).
        // Safe areas are handled by CSS via viewport-fit=cover and env(safe-area-inset-*).
    }

    private partial Task WhenPlatformWebViewReady()
        // WKWebView has no provider load to wait for.
        => Task.CompletedTask;
}

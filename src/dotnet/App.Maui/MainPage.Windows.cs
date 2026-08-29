namespace ActualChat.App.Maui;

public partial class MainPage
{
    private partial void OnLoaded_Platform()
    { }

    private partial Task WhenPlatformWebViewReady()
        // WebView2 has no provider load to wait for.
        => Task.CompletedTask;
}

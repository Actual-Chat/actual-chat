namespace ActualChat.App.Maui;

public partial class MainPage
{
    private partial void OnLoaded_Platform()
    { }

    private partial Task WhenPlatformWebViewReady()
        // WebKitGTK has no provider load to wait for.
        => Task.CompletedTask;
}

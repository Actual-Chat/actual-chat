using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

public class MauiAudioInitializer: IAudioInitializer
{
    public void StartInitialization()
    { }

    public Task WhenInitialized { get; } = Task.CompletedTask;
}

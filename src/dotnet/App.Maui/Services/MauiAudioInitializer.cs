using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="IAudioInitializer"/> - no-op since native audio is always ready.
/// </summary>
public class MauiAudioInitializer: IAudioInitializer
{
    public void StartInitialization()
    { }

    public Task WhenInitialized { get; } = Task.CompletedTask;
}

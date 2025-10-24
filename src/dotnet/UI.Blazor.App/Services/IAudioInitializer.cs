namespace ActualChat.UI.Blazor.App.Services;

public interface IAudioInitializer
{
    void StartInitialization();

    Task WhenInitialized { get; }
}

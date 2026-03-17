namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioPlayerBackend
{
    void OnPlaying(double offset, bool isPaused, bool isBufferLow);
    void OnEnded(string? errorMessage);
}

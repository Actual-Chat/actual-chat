namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioPlayerBackend
{
    void OnPlaying(double offset, bool isPaused, bool isBufferLow);
    void OnPresentationLag(TimeSpan lag);
    void OnEnded(string? errorMessage);
}

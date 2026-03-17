namespace ActualChat.UI.Blazor.App.Components;

public interface IVideoPlayerBackend
{
    void OnPlaying(double offset, bool isBufferLow);
    void OnEnded(string? errorMessage);
}

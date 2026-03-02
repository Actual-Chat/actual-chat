namespace ActualChat.UI.Blazor.App.Components;

public interface IVideoPlayerBackend
{
    Task OnPlaying(double offset, bool isBufferLow);
    Task OnEnded(string? errorMessage);
}

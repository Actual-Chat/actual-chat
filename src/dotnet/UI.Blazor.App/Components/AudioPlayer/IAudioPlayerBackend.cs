namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioPlayerBackend
{
    Task OnPlaying(double offset, bool isPaused, bool isBufferLow);
    Task OnEnded(string? errorMessage);
}

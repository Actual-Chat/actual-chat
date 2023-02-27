namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioPlayerBackend
{
    Task OnBufferStateChange(bool isBufferLow);
    Task OnPlayingAt(double offset);
    Task OnPausedAt(double offset);
    Task OnEnded(string? errorMessage);
}

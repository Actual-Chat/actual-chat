namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioPlayerBackend
{
    Task OnChangeReadiness(bool isBufferReady);
    Task OnPlayEnded(string? errorMessage);
    Task OnPlayTimeChanged(double offset);
    Task OnPausedAt(double offset);
}

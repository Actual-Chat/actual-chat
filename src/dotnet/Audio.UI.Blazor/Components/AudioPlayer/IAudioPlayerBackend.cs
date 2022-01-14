namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioPlayerBackend
{
    Task OnChangeReadiness(bool isBufferReady);
    Task OnPlaybackEnded(string? errorMessage);
    Task OnPlaybackTimeChanged(double offset);
}

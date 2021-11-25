namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioPlayerBackend
{
    Task OnChangeReadiness(bool isbufferReady, double? offset, int? readyState);
    Task OnPlaybackEnded(int? errorCode, string? errorMessage);
    Task OnPlaybackTimeChanged(double? offset);
}

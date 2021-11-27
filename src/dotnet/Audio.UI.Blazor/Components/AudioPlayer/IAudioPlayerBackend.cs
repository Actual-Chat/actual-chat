namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioPlayerBackend
{
    Task OnChangeReadiness(bool isBufferReady, double? offset, int? readyState);
    Task OnPlaybackEnded(int? errorCode, string? errorMessage);
    Task OnPlaybackTimeChanged(double? offset);
}

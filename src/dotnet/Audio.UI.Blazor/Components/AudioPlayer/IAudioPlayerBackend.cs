namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioPlayerBackend
{
    Task OnDataWaiting(double? offset, int? readyState);
    Task OnPlaybackEnded(int? errorCode, string? errorMessage);
    Task OnPlaybackTimeChanged(double? offset);
}

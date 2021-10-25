namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioPlayerBackend
{
    void OnDataWaiting(double offset, int readyState);
    void OnPlaybackEnded(int? errorCode, string? errorMessage);
    void OnPlaybackTimeChanged(double offset);
}

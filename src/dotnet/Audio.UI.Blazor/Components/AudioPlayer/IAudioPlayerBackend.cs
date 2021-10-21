namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioPlayerBackend
{
    void OnPlaybackTimeChanged(double offset);
    void OnPlaybackEnded(int? errorCode, string? errorMessage);
}

namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioPlayerBackend
{
    void SetCurrentPlaybackTime(double offsetSeconds);
}

namespace ActualChat.Audio.UI.Blazor.Internal;

public interface IAudioRecorderBackend
{
    void OnStartRecording();
    void OnAudioData(byte[] chunk);
    void OnStopRecording();
}

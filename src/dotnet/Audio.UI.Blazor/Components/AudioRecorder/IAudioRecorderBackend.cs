namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioRecorderBackend
{
    void OnStartRecording();
    void OnAudioData(byte[] chunk);
    void OnStopRecording();
}

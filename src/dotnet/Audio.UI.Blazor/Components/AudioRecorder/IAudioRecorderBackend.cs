namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioRecorderBackend
{
    void OnStartRecording();
    Task OnAudioEventChunk(byte[] chunk);
    void OnRecordingStopped();
}

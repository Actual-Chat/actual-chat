namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioRecorderBackend
{
    void OnStartRecording();
    Task OnAudioData(byte[] chunk);
    void OnRecordingStopped();
    void OnPauseRecording();
    void OnResumeRecording();

}

namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioRecorderBackend
{
    void OnRecordingStarted(string chatId);
    Task OnRecordingStopped();
}

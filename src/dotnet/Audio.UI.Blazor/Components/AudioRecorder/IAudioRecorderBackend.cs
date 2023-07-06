namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioRecorderBackend
{
    [JSInvokable]
    void OnRecordingStateChange(bool isRecording, bool isConnected, bool isVoiceActive);
}

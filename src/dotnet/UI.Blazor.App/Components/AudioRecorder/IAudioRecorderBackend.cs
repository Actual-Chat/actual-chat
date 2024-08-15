namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioRecorderBackend
{
    [JSInvokable]
    void OnRecordingStateChange(bool isRecording, bool isConnected, bool isVoiceActive);
}

namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioRecorderBackend
{
    [JSInvokable]
    void OnRecordingStateChange(bool isRecording, bool isSignalDetected, bool isConnected, bool isVoiceActive);
}

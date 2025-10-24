namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioRecorderBackend
{
    [JSInvokable]
    bool IsRecording(string chatId);

    [JSInvokable]
    void OnRecordingStateChange(bool isRecording, bool isSignalDetected, bool isConnected, bool isVoiceActive);
}

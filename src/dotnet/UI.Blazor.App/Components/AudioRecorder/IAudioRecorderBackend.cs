namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioRecorderBackend
{
    [JSInvokable]
    bool IsRecording(string chatId);

    [JSInvokable]
    void OnRecordingStateChange(bool isRecording, bool isSignalDetected, bool isConnected, bool isVoiceActive);

    // For pipelines that only learn of a failure after Start returned - AVAudioEngine builds its
    // graph inside the capture iterator. `failure` is RecorderStartOutcome's wire form.
    [JSInvokable]
    void OnRecordingFailed(string chatId, string failure);
}

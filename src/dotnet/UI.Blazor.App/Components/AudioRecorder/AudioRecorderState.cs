namespace ActualChat.UI.Blazor.App.Components;

[StructLayout(LayoutKind.Auto)]
public readonly record struct AudioRecorderState(
    ChatId ChatId,
    bool IsRecording = false,
    bool IsConnected = false,
    bool IsVoiceActive = false)
{
    public Moment RecordingStartTime { get; init; }
    public static readonly AudioRecorderState Idle = default;
}

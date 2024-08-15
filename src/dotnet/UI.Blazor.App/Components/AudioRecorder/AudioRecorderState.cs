namespace ActualChat.UI.Blazor.App.Components;

[StructLayout(LayoutKind.Auto)]
public readonly record struct AudioRecorderState(
    ChatId ChatId,
    bool IsRecording = false,
    bool IsConnected = false,
    bool IsVoiceActive = false)
{
    public static readonly AudioRecorderState Idle = default;
}

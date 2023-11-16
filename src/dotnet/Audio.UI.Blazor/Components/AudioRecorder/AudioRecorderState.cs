namespace ActualChat.Audio.UI.Blazor.Components;

[StructLayout(LayoutKind.Auto)]
public readonly record struct AudioRecorderState(
    ChatId ChatId,
    bool IsRecording = false,
    bool IsConnected = false,
    bool IsVoiceActive = false)
{
    public static readonly AudioRecorderState Idle = default;

    public bool RequiresRecordingTroubleshooter()
        => !ChatId.IsNone && !IsRecording && IsConnected;
        // Good for debugging:
        // => !ChatId.IsNone && !IsVoiceActive;
}

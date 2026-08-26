namespace ActualChat.UI.Blazor.App.Components;

public sealed record AudioRecorderState(ChatId? ChatId)
{
    public static readonly AudioRecorderState Idle = new((ChatId?)null);

    public bool IsRecording { get; init; }
    public bool IsSignalDetected { get; init; }
    public bool IsConnected { get; init; }
    public bool IsVoiceActive { get; init; }
    public Moment RecordingStartTime { get; init; }
    // CPU time, unlike RecordingStartTime above: it's compared against UI-side elapsed times.
    public Moment ChangedAt { get; init; }
    public RecordingFailure? LastFailure { get; init; }

    public void Deconstruct(
        out ChatId? chatId,
        out bool isRecording,
        out bool isSignalDetected,
        out bool isConnected,
        out bool isVoiceActive)
    {
        chatId = ChatId;
        isRecording = IsRecording;
        isSignalDetected = IsSignalDetected;
        isConnected = IsConnected;
        isVoiceActive = IsVoiceActive;
    }
}

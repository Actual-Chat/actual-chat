namespace ActualChat.UI.Blazor.App.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ChatAudioState(
    ChatId ChatId,
    bool IsListening = false,
    bool IsPlayingHistorical = false,
    bool IsRecording = false
    ) : ICanBeNone<ChatAudioState>
{
    public static ChatAudioState None { get; } = default;

    public bool IsNone => ChatId.IsNone;
}

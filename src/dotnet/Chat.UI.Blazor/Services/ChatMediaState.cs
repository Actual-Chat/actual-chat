namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ChatMediaState(
    ChatId ChatId,
    bool IsListening = false,
    bool IsPlayingHistorical = false,
    bool IsRecording = false
    ) : ICanBeNone<ChatMediaState>
{
    public static ChatMediaState None { get; } = default;

    public bool IsNone => ChatId.IsNone;
}

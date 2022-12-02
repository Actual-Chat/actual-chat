namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ChatMediaState(
    ChatId ChatId,
    bool IsListening,
    bool IsPlayingHistorical,
    bool IsRecording
    ) : ICanBeNone<ChatMediaState>
{
    public static ChatMediaState None { get; } = default;

    public bool IsNone => ChatId.IsNone;
}

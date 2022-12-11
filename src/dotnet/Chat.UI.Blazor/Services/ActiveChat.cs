namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ActiveChat(
    ChatId ChatId,
    bool IsListening = false,
    bool IsRecording = false,
    Moment Recency = default)
{
    public static implicit operator ActiveChat(ChatId chatId) => new(chatId);

    // Equality must rely on Id only
    public bool Equals(ActiveChat other)
        => ChatId.Equals(other.ChatId);
    public override int GetHashCode()
        => ChatId.GetHashCode();
}

namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ActiveChat(
    Symbol ChatId,
    bool IsListening,
    bool IsRecording,
    Moment Recency)
{
    public static implicit operator ActiveChat(string chatId)
        => new(chatId, false, false, default);
    public static implicit operator ActiveChat(Symbol chatId)
        => new(chatId, false, false, default);

    // Equality must rely on ChatId only

    public bool Equals(ActiveChat other)
        => ChatId.Equals(other.ChatId);
    public override int GetHashCode()
        => ChatId.GetHashCode();
}

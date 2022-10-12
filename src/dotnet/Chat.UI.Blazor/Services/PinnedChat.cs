namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct PinnedChat(
    Symbol ChatId,
    Moment Recency)
{
    public static implicit operator PinnedChat(string chatId)
        => new(chatId, default);
    public static implicit operator PinnedChat(Symbol chatId)
        => new(chatId, default);

    // Equality must rely on ChatId only

    public bool Equals(ActiveChat other)
        => ChatId.Equals(other.ChatId);

    public override int GetHashCode()
        => ChatId.GetHashCode();
}

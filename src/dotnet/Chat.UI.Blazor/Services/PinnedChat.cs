namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct PinnedChat(
    ChatId ChatId,
    Moment Recency = default)
{
    public static implicit operator PinnedChat(ChatId chatId) => new(chatId);

    // Equality must rely on Id only
    public bool Equals(PinnedChat other)
        => ChatId.Equals(other.ChatId);
    public override int GetHashCode()
        => ChatId.GetHashCode();
}

namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
[DataContract]
public readonly record struct PinnedChat(
    [property: DataMember] ChatId ChatId,
    [property: DataMember] Moment Recency = default)
{
    public static implicit operator PinnedChat(ChatId chatId) => new(chatId);

    // Equality must rely on Id only
    public bool Equals(PinnedChat other)
        => ChatId.Equals(other.ChatId);
    public override int GetHashCode()
        => ChatId.GetHashCode();
}

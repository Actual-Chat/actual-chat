using MemoryPack;

namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial record struct PinnedChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] Moment Recency = default)
{
    public static implicit operator PinnedChat(ChatId chatId) => new(chatId);

    // Equality must rely on Id only
    public bool Equals(PinnedChat other)
        => ChatId.Equals(other.ChatId);
    public override int GetHashCode()
        => ChatId.GetHashCode();
}

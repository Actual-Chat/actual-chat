using MemoryPack;

namespace ActualChat.Audio;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AudioRecord(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id, // Ignored on upload
    [property: DataMember, MemoryPackOrder(1)] Session Session,
    [property: DataMember, MemoryPackOrder(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3)] double ClientStartOffset,
    [property: DataMember, MemoryPackOrder(4)] ChatEntryId RepliedChatEntryId
    ) : IHasId<Symbol>
{
    private static Symbol NewId() => Ulid.NewUlid().ToString();

    public static AudioRecord New(
        Session session,
        ChatId chatId,
        double clientStartOffset,
        ChatEntryId repliedChatEntryId)
        => new (NewId(), session, chatId, clientStartOffset, repliedChatEntryId);

    // This record relies on referential equality
    public bool Equals(AudioRecord? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

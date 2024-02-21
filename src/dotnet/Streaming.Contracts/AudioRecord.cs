using MemoryPack;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AudioRecord(
    [property: DataMember, MemoryPackOrder(0)] StreamId StreamId, // Ignored on upload
    [property: DataMember, MemoryPackOrder(1)] Session Session,
    [property: DataMember, MemoryPackOrder(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3)] double ClientStartOffset,
    [property: DataMember, MemoryPackOrder(4)] ChatEntryId RepliedChatEntryId
    ) : IHasId<StreamId>, IHasNodeRef
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    NodeRef IHasNodeRef.NodeRef => StreamId.NodeRef;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    StreamId IHasId<StreamId>.Id => StreamId;

    // This record relies on referential equality
    public bool Equals(AudioRecord? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

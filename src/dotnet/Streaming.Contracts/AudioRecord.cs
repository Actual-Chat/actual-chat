namespace ActualChat.Streaming;

/// <summary>
/// Represents an active audio recording session for a chat entry.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[method: MemoryPackConstructor, SerializationConstructor]
public sealed partial record AudioRecord(
    [property: DataMember, MemoryPackOrder(0), Key(0)] StreamId StreamId, // Ignored on upload
    [property: DataMember, MemoryPackOrder(1), Key(1)] Session Session,
    [property: DataMember, MemoryPackOrder(2), Key(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3), Key(3)] double ClientStartAt, // Unix epoch (seconds, double)
    [property: DataMember, MemoryPackOrder(4), Key(4)] ChatEntryId? RepliedEntryId
    ) : IHasId<StreamId>, IHasNodeRef
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    NodeRef IHasNodeRef.NodeRef => StreamId.NodeRef;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    StreamId IHasId<StreamId>.Id => StreamId;

    // This record relies on referential equality
    public bool Equals(AudioRecord? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

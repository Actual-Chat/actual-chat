namespace ActualChat.Streaming;

/// <summary>
/// Represents an active audio recording session for a chat entry.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record AudioRecord(
    [property: DataMember, Key(0)] StreamId StreamId, // Ignored on upload
    [property: DataMember, Key(1)] Session Session,
    [property: DataMember, Key(2)] ChatId ChatId,
    [property: DataMember, Key(3)] double ClientStartAt, // Unix epoch (seconds, double)
    [property: DataMember, Key(4)] ChatEntryId? RepliedEntryId
    ) : IHasId<StreamId>, IHasNodeRef
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    NodeRef IHasNodeRef.NodeRef => StreamId.NodeRef;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    StreamId IHasId<StreamId>.Id => StreamId;

    // This record relies on referential equality
    public bool Equals(AudioRecord? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

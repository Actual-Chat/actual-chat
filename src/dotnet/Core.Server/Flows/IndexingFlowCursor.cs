namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record IndexingFlowCursor<TId>(
    // TODO(AY): Serialization(LastUpdatedId)
    [property: DataMember, MemoryPackOrder(0), Key(0)] TId? LastUpdatedId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long LastUpdatedVersion)
    where TId : StringIdentifier;

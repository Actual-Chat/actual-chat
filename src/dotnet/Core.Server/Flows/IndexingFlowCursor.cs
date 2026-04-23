namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record IndexingFlowCursor<TId>(
    // TODO(AY): Serialization(LastUpdatedId)
    [property: DataMember, MemoryPackOrder(0), NbKey(0)] TId? LastUpdatedId,
    [property: DataMember, MemoryPackOrder(1), NbKey(1)] long LastUpdatedVersion)
    where TId : StringIdentifier;

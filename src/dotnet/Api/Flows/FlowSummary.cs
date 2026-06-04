namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record FlowSummary(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string FlowId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Name,
    [property: DataMember, MemoryPackOrder(2), Key(2)] FlowStatus Status,
    [property: DataMember, MemoryPackOrder(3), Key(3)] long Version,
    [property: DataMember, MemoryPackOrder(4), Key(4)] Moment UpdatedAt);

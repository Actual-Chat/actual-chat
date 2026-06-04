namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record FlowsReport(
    [property: DataMember, MemoryPackOrder(0), Key(0)] FlowTypeStat[] Aggregates,
    [property: DataMember, MemoryPackOrder(1), Key(1)] FlowSummary[] Rows,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Moment GeneratedAt);

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record FlowDetails(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string Console,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string? Error);

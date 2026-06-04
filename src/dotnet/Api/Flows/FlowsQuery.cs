namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record FlowsQuery(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string? Name = null,
    [property: DataMember, MemoryPackOrder(1), Key(1)] bool ProblematicOnly = false,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int Limit = 100);

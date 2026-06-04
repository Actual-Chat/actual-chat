namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record FlowTypeStat(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string Name,
    [property: DataMember, MemoryPackOrder(1), Key(1)] int Active,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int Completed,
    [property: DataMember, MemoryPackOrder(3), Key(3)] int Failed,
    [property: DataMember, MemoryPackOrder(4), Key(4)] int Stuck)
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int Total => Active + Completed + Failed + Stuck;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int Problematic => Failed + Stuck;
}

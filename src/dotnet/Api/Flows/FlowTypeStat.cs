namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record FlowTypeStat(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string Name,
    [property: DataMember, MemoryPackOrder(1), Key(1)] int Completed,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int Failed,
    [property: DataMember, MemoryPackOrder(3), Key(3)] int Suspended,
    [property: DataMember, MemoryPackOrder(4), Key(4)] int Stuck,
    [property: DataMember, MemoryPackOrder(5), Key(5)] int Idle)
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int Total => Completed + Failed + Suspended + Stuck + Idle;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int Problematic => Failed + Stuck;
}

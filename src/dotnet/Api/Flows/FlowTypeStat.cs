namespace ActualChat.Flows;

[DataContract, MessagePackObject]
public sealed partial record FlowTypeStat(
    [property: DataMember, Key(0)] string Name,
    [property: DataMember, Key(1)] int Completed,
    [property: DataMember, Key(2)] int Failed,
    [property: DataMember, Key(3)] int Scheduled,
    [property: DataMember, Key(4)] int Stuck,
    [property: DataMember, Key(5)] int Idle)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int Total => Completed + Failed + Scheduled + Stuck + Idle;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int Problematic => Failed + Stuck;
}

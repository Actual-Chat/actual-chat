namespace ActualChat.Flows;

// Failed is a part of Completed, and MaybeStuck - of Idle: an idle flow past its type's MaxResumeGap,
// which only an instance check (FlowBackend.List) tells stuck or not

[DataContract, MessagePackObject]
public sealed partial record FlowTypeStat(
    [property: DataMember, Key(0)] string Name,
    [property: DataMember, Key(1)] int Completed,
    [property: DataMember, Key(2)] int Failed,
    [property: DataMember, Key(3)] int Scheduled,
    [property: DataMember, Key(5)] int Idle,
    [property: DataMember, Key(6)] int MaybeStuck)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int Total => Completed + Scheduled + Idle;
}

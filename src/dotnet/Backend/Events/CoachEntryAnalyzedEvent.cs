namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record CoachEntryAnalyzedEvent(
    [property: DataMember] CoachEntryAnalysis Analysis,
    [property: DataMember] bool IsRemoved
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Analysis.UserId.ShardKey;
}

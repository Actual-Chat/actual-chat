namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record CoachConversationAnalyzedEvent(
    [property: DataMember] CoachConversationAnalysis Analysis
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Analysis.UserId.ShardKey;
}

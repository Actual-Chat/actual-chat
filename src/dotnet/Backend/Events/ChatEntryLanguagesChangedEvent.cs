namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ChatEntryLanguagesChangedEvent(
    [property: DataMember] ChatEntryLanguage[] EntryLanguages
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => EntryLanguages.FirstOrDefault()?.Id.ShardKey ?? default;
}

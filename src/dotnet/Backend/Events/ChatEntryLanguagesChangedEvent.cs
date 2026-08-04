namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ChatEntryLanguagesChangedEvent(
    [property: DataMember] ChatEntryLanguage[] EntryLanguages
) : EventCommand, IHasShardKey<ChatEntryId?>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatEntryId? ShardKey => EntryLanguages.FirstOrDefault()?.Id;
}

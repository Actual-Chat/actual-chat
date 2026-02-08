using ActualChat.Chat;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record ChatEntryLanguagesChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] ChatEntryLanguage[] EntryLanguages
) : EventCommand, IHasShardKey<ChatEntryId?>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatEntryId? ShardKey => EntryLanguages.FirstOrDefault()?.Id;
}

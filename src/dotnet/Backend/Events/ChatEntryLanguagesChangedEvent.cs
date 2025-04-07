using ActualChat.Chat;
using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatEntryLanguagesChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] ChatEntryLanguage[] EntryLanguages
) : EventCommand, IHasShardKey<ChatEntryId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId ShardKey => EntryLanguages.FirstOrDefault()?.Id ?? ChatEntryId.None;
}

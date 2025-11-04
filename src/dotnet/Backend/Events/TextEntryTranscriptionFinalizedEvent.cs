using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record TextEntryTranscriptionFinalizedEvent(
    [property: DataMember, MemoryPackOrder(1)] ChatEntryId EntryId
) : EventCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => EntryId.ChatId;
}

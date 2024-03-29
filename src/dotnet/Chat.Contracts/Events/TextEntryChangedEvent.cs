using MemoryPack;

namespace ActualChat.Chat.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record TextEntryChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] ChatEntry Entry,
    [property: DataMember, MemoryPackOrder(2)] AuthorFull Author,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<ChatEntryId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId ShardKey => Entry.Id;
}

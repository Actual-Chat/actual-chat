namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record ChatEntryChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] ChatEntry Entry,
    [property: DataMember, MemoryPackOrder(2)] AuthorFull Author,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind,
    [property: DataMember, MemoryPackOrder(4)] ChatEntry? OldEntry
) : EventCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => Entry.ChatId;
}

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record AuthorUpsertedEvent(
    [property: DataMember, MemoryPackOrder(1)] AuthorFull Author,
    [property: DataMember, MemoryPackOrder(2)] AuthorFull? OldAuthor
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => Author.ChatId;
}

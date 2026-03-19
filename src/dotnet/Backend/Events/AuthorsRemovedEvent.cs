namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record AuthorsRemovedEvent(
    [property: DataMember, MemoryPackOrder(1)] AuthorFull[] Authors
) : EventCommand, IHasShardKey<ChatId?>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId? ShardKey => Authors.Length > 0 ? Authors[0].ChatId : null;
}

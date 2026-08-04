namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record AuthorsRemovedEvent(
    [property: DataMember] AuthorFull[] Authors
) : EventCommand, IHasShardKey<ChatId?>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId? ShardKey => Authors.Length > 0 ? Authors[0].ChatId : null;
}

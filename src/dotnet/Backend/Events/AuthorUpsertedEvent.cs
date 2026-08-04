namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record AuthorUpsertedEvent(
    [property: DataMember] AuthorFull Author,
    [property: DataMember] AuthorFull? OldAuthor
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Author.ChatId;
}

namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ReactionChangedEvent(
    [property: DataMember] Reaction Reaction,
    [property: DataMember] ChatEntry Entry,
    [property: DataMember] AuthorFull Author,
    [property: DataMember] AuthorFull ReactionAuthor,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Entry.ChatId;
}

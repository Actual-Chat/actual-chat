namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record ChatChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] Chat.Chat Chat,
    [property: DataMember, MemoryPackOrder(2)] Chat.Chat? OldChat,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => Chat.Id;
}

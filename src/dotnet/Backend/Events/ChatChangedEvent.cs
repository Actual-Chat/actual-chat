using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] Chat.Chat Chat,
    [property: DataMember, MemoryPackOrder(2)] Chat.Chat? OldChat,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Chat.Id;
}

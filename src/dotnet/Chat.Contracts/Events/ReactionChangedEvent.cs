using ActualChat.Commands;
using MemoryPack;

namespace ActualChat.Chat.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ReactionChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] Reaction Reaction,
    [property: DataMember, MemoryPackOrder(2)] ChatEntry Entry,
    [property: DataMember, MemoryPackOrder(3)] AuthorFull Author,
    [property: DataMember, MemoryPackOrder(4)] AuthorFull ReactionAuthor,
    [property: DataMember, MemoryPackOrder(5)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Entry.ChatId;
}

using ActualChat.Chat;
using MemoryPack;

namespace ActualChat.Backend.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AuthorChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] AuthorFull Author,
    [property: DataMember, MemoryPackOrder(2)] AuthorFull? OldAuthor
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Author.ChatId;
}

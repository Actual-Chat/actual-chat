using ActualChat.Chat;
using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AuthorsRemovedEvent(
    [property: DataMember, MemoryPackOrder(1)] AuthorFull[] Authors
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Authors.Length > 0 ? Authors[0].ChatId : ChatId.None;
}

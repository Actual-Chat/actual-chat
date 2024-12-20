using ActualChat.Roulette;
using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatRouletteCompletedEvent(
    [property: DataMember, MemoryPackOrder(1)] ChatRouletteFull ChatRoulette
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatRoulette.ChatId;
}

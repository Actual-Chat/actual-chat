using MemoryPack;

namespace ActualChat.Chat.Db;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record DbChatEntryShardRef(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] ChatEntryKind Kind)
{
    public override string ToString() => $"{ChatId}:{Kind}";
}

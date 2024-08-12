using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChangedChatsQuery
{
    [DataMember, MemoryPackOrder(0)] public long MinVersion { get; init; }
    [DataMember, MemoryPackOrder(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, MemoryPackOrder(2)] public ChatId LastId { get; init; }
    [DataMember, MemoryPackOrder(3)] public int Limit { get; init; }
    [DataMember, MemoryPackOrder(4)] public bool ExcludePeerChats { get; init; }
    [DataMember, MemoryPackOrder(5)] public bool ExcludePlaceRootChats { get; init; }
}

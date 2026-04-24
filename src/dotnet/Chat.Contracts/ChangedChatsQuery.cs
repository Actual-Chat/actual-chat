namespace ActualChat.Chat;

/// <summary>
/// Query parameters for listing changed chats by version range.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ChangedChatsQuery
{
    [DataMember, MemoryPackOrder(2), Key(2)] public required ChatId? LastId { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public required int Limit { get; init; }
    [DataMember, MemoryPackOrder(0), Key(0)] public long MinVersion { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, MemoryPackOrder(4), Key(4)] public bool ExcludePeerChats { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public bool ExcludePlaceRootChats { get; init; }
}

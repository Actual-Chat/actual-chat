namespace ActualChat.Chat;

/// <summary>
/// Query parameters for listing changed chat entries by version range.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ChangedEntriesQuery
{
    [DataMember, MemoryPackOrder(0), Key(0)] public long MinVersion { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, MemoryPackOrder(2), Key(2)] public long LastLocalId { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public ChatId ChatId { get; init; } = null!;
    [DataMember, MemoryPackOrder(4), Key(4)] public int Limit { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public bool RequireAttachments { get; init; }
}

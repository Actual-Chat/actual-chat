namespace ActualChat.Contacts;

/// <summary>
/// Query parameters for listing changed contacts by version range.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ChangedContactsQuery
{
    [DataMember, MemoryPackOrder(2), Key(2)] public required ContactId? LastId { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public required int Limit { get; init; }
    [DataMember, MemoryPackOrder(0), Key(0)] public long MinVersion { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public long MaxVersion { get; init; } = long.MaxValue;
}

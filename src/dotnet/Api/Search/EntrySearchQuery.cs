namespace ActualChat.Search;

/// <summary>
/// Query parameters for searching chat entries.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record EntrySearchQuery
{
    [DataMember, MemoryPackOrder(0), Key(0)] public string Criteria { get; init; } = "";
    [DataMember, MemoryPackOrder(1), Key(1)] public PlaceId? PlaceId { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public ChatId? ChatId { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public int Skip { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public int Limit { get; init; } = Constants.Search.DefaultPageSize;
}

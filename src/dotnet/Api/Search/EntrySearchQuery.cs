namespace ActualChat.Search;

/// <summary>
/// Query parameters for searching chat entries.
/// </summary>
[DataContract, MessagePackObject]
public partial record EntrySearchQuery
{
    [DataMember, Key(0)] public string Criteria { get; init; } = "";
    [DataMember, Key(1)] public PlaceId? PlaceId { get; init; }
    [DataMember, Key(2)] public ChatId? ChatId { get; init; }
    [DataMember, Key(3)] public int Skip { get; init; }
    [DataMember, Key(4)] public int Limit { get; init; } = Constants.Search.DefaultPageSize;
    [DataMember, Key(5)] public bool OwnEntriesOnly { get; init; }
}

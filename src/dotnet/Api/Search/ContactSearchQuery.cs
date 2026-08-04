namespace ActualChat.Search;

/// <summary>
/// Query parameters for searching contacts.
/// </summary>
[DataContract, MessagePackObject]
public partial record ContactSearchQuery
{
    [DataMember, Key(4)] public SearchScope Scope { get; init; }
    [DataMember, Key(0)] public string Criteria { get; init; } = "";
    [DataMember, Key(1)] public PlaceId? PlaceId { get; init; }
    [DataMember, Key(5)] public bool Own { get; init; }
    [DataMember, Key(2)] public int Skip { get; init; }
    [DataMember, Key(3)] public int Limit { get; init; } = Constants.Search.DefaultPageSize;

    [IgnoreDataMember, IgnoreMember]
    [MemberNotNullWhen(true, nameof(PlaceId))]
    public bool MustFilterByPlace => PlaceId is not null;
}

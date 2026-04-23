namespace ActualChat.Search;

/// <summary>
/// Query parameters for searching contacts.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ContactSearchQuery
{
    [DataMember, MemoryPackOrder(6), Key(4)] public SearchScope Scope { get; init; }
    [DataMember, MemoryPackOrder(1), Key(0)] public string Criteria { get; init; } = "";
    [DataMember, MemoryPackOrder(2), Key(1)] public PlaceId? PlaceId { get; init; }
    [DataMember, MemoryPackOrder(7), Key(5)] public bool Own { get; init; }
    [DataMember, MemoryPackOrder(4), Key(2)] public int Skip { get; init; }
    [DataMember, MemoryPackOrder(5), Key(3)] public int Limit { get; init; } = Constants.Search.DefaultPageSize;

    [IgnoreDataMember, MemoryPackIgnore]
    [MemberNotNullWhen(true, nameof(PlaceId))]
    public bool MustFilterByPlace => PlaceId is not null;
}

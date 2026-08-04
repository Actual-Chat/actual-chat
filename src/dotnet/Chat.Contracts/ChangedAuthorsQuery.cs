namespace ActualChat.Chat;

/// <summary>
/// Query parameters for listing changed authors by version range.
/// </summary>
[DataContract, MessagePackObject]
public partial record ChangedAuthorsQuery
{
    [DataMember, Key(0)] public long MinVersion { get; init; }
    [DataMember, Key(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, Key(2)] public AuthorId? LastId { get; init; }
    [DataMember, Key(3)] public int Limit { get; init; }
    [DataMember, Key(4)] public bool? IsPlaceAuthor { get; init; }
}

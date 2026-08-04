namespace ActualChat.Search;

/// <summary>
/// A paginated collection of search results.
/// </summary>
[DataContract, MessagePackObject]
public partial class SearchResult<TItem>
    where TItem : class, IHasSearchMatch
{
    public static readonly SearchResult<TItem> Empty = new();

    [DataMember, Key(0)] public TItem[] Items { get; init; } = [];
    [DataMember, Key(1)] public int Offset { get; init; }
}

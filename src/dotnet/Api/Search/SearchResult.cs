namespace ActualChat.Search;

/// <summary>
/// A paginated collection of search results.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial class SearchResult<TItem>
    where TItem : class, IHasSearchMatch
{
    public static readonly SearchResult<TItem> Empty = new();

    [DataMember, MemoryPackOrder(0), Key(0)] public TItem[] Items { get; init; } = [];
    [DataMember, MemoryPackOrder(1), Key(1)] public int Offset { get; init; }
}

namespace ActualChat.Search;

/// <summary>
/// A paginated collection of chat entry search results.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class EntrySearchResultPage
{
    public static readonly EntrySearchResultPage Empty = new ();
    [DataMember, MemoryPackOrder(0)] public EntrySearchResult[] Hits { get; init; } = [];
    [DataMember, MemoryPackOrder(1)] public int Offset { get; init; }
}

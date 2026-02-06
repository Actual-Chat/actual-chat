using MemoryPack;

namespace ActualChat.Search;

/// <summary>
/// A paginated collection of contact search results.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ContactSearchResultPage
{
    public static readonly ContactSearchResultPage Empty = new ();
    [DataMember, MemoryPackOrder(0)] public ContactSearchResult[] Hits { get; init; } = [];
    [DataMember, MemoryPackOrder(1)] public int Offset { get; init; }
}

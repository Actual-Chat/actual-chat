using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntrySearchResultPage
{
    public static readonly EntrySearchResultPage Empty = new ();
    [DataMember, MemoryPackOrder(0)] public ApiArray<EntrySearchResult> Hits { get; init; }
    [DataMember, MemoryPackOrder(1)] public int Offset { get; init; }
}

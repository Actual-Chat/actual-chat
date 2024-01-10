using MemoryPack;
using ActualLab.Fusion.Extensions;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class SearchResultPage
{
    public static readonly SearchResultPage Empty = new ();
    [DataMember, MemoryPackOrder(0)] public ApiArray<EntrySearchResult> Hits { get; init; }
    [DataMember, MemoryPackOrder(1)] public int Offset { get; init; }
}

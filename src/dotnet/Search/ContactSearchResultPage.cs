using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ContactSearchResultPage
{
    public static readonly ContactSearchResultPage Empty = new ();
    [DataMember, MemoryPackOrder(0)] public ApiArray<ContactSearchResult> Hits { get; init; }
    [DataMember, MemoryPackOrder(1)] public int Offset { get; init; }
}

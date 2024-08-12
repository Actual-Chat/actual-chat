using System.Diagnostics.CodeAnalysis;
using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ContactSearchQuery
{
    [DataMember, MemoryPackOrder(6)] public ContactSearchScope Scope { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Criteria { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public PlaceId? PlaceId { get; init; }
    [DataMember, MemoryPackOrder(7)] public bool Own { get; init; }
    [DataMember, MemoryPackOrder(4)] public int Skip { get; init; }
    [DataMember, MemoryPackOrder(5)] public int Limit { get; init; } = Constants.Search.ContactSearchDefaultPageSize;
    [IgnoreDataMember, MemoryPackIgnore]
    [MemberNotNullWhen(true, nameof(PlaceId))]
    public bool MustFilterByPlace => PlaceId != null && PlaceId != ActualChat.PlaceId.None;
}

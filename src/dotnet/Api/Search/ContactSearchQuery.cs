using System.Diagnostics.CodeAnalysis;
using ActualChat.Contacts;
using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ContactSearchQuery
{
    [Obsolete("2024.06: Never used by non-admin clients, can be removed after a while")]
    // TODO: remove after all admin clients are upgraded
    [DataMember, MemoryPackOrder(0)] public ContactKind Kind { get; init; }
    [DataMember, MemoryPackOrder(6)] public ContactSearchScope Scope { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Criteria { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public PlaceId? PlaceId { get; init; }
    [Obsolete("2024.06: Never used by non-admin clients, can be removed after a while")]
    // TODO: remove after all admin clients are upgraded
    [DataMember, MemoryPackOrder(3)] public bool IsPublic { get; init; }
    [DataMember, MemoryPackOrder(7)] public bool Own { get; init; }
    [DataMember, MemoryPackOrder(4)] public int Skip { get; init; }
    [DataMember, MemoryPackOrder(5)] public int Limit { get; init; } = Constants.Search.ContactSearchDefaultPageSize;
    [IgnoreDataMember, MemoryPackIgnore]
    [MemberNotNullWhen(true, nameof(PlaceId))]
    public bool MustFilterByPlace => PlaceId != null && PlaceId != ActualChat.PlaceId.None;
}

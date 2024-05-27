using ActualChat.Contacts;
using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ContactSearchQuery
{
    [DataMember, MemoryPackOrder(0)] public ContactKind Kind { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Criteria { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public PlaceId? PlaceId { get; init; }
    [DataMember, MemoryPackOrder(3)] public bool IsPublic { get; init; }
    [DataMember, MemoryPackOrder(4)] public int Skip { get; init; }
    [DataMember, MemoryPackOrder(5)] public int Limit { get; init; } = Constants.Search.ContactSearchDeafultPageSize;
}

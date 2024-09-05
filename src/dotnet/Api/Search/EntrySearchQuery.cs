using System.Diagnostics.CodeAnalysis;
using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record EntrySearchQuery
{
    [DataMember, MemoryPackOrder(0)] public string Criteria { get; init; } = "";
    [DataMember, MemoryPackOrder(1)] public PlaceId? PlaceId { get; init; }
    [DataMember, MemoryPackOrder(2)] public ChatId ChatId { get; init; }
    [DataMember, MemoryPackOrder(3)] public int Skip { get; init; }
    [DataMember, MemoryPackOrder(4)] public int Limit { get; init; } = Constants.Search.ContactSearchDefaultPageSize;
}

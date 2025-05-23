using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChangedAuthorsQuery
{
    [DataMember, MemoryPackOrder(0)] public long MinVersion { get; init; }
    [DataMember, MemoryPackOrder(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, MemoryPackOrder(2)] public AuthorId? LastId { get; init; }
    [DataMember, MemoryPackOrder(3)] public int Limit { get; init; }
    [DataMember, MemoryPackOrder(4)] public bool? IsPlaceAuthor { get; init; }
}

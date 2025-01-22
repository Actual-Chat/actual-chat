using MemoryPack;

namespace ActualChat.Contacts;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChangedContactsQuery
{
    [DataMember, MemoryPackOrder(0)] public long MinVersion { get; init; }
    [DataMember, MemoryPackOrder(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, MemoryPackOrder(2)] public ContactId LastId { get; init; }
    [DataMember, MemoryPackOrder(3)] public int Limit { get; init; }
}

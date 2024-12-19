using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Roulette;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatRoulette(
    [property: DataMember, MemoryPackOrder(0)] ChatRouletteId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0)
    : IHasId<ChatRouletteId>, IHasVersion<long>
{
    public static readonly MediaId MediaId = new ("system-icons:chatroulette");

    [DataMember, MemoryPackOrder(2)] public ChatId ChatId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol ProfileId1 => Id.ProfileId1;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol ProfileId2 => Id.ProfileId2;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatRouletteProfiles([property: DataMember, MemoryPackOrder(0)] ChatRoulette ChatRoulette)
{
    [DataMember, MemoryPackOrder(1)] public Profile OwnProfile { get; init; } = null!;
    [DataMember, MemoryPackOrder(2)] public Profile PeerProfile { get; init; } = null!;
}

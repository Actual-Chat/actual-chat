using MemoryPack;

namespace ActualChat.Roulette;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatRouletteProfiles([property: DataMember, MemoryPackOrder(0)] ChatRoulette ChatRoulette)
{
    [DataMember, MemoryPackOrder(1)] public Profile OwnProfile { get; init; } = null!;
    [DataMember, MemoryPackOrder(2)] public Profile PeerProfile { get; init; } = null!;
}

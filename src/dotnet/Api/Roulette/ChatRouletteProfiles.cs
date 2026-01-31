using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Roulette;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ChatRouletteProfiles(
    [property: DataMember, MemoryPackOrder(0)] ChatRoulette ChatRoulette)
{
    [DataMember, MemoryPackOrder(1)] public Profile? OwnProfile { get; init; }
    [DataMember, MemoryPackOrder(2)] public Profile? PeerProfile { get; init; }

    // This record relies on referential equality
    public bool Equals(ChatRouletteProfiles? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

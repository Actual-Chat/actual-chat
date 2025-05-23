using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Roulette;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ChatCandidate(
    [property: DataMember, MemoryPackOrder(0)]
    Profile Profile)
{
    // This record relies on referential equality
    public bool Equals(ChatCandidate? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
};

using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Picture(
    [property: DataMember, MemoryPackOrder(0)] MediaContent? MediaContent,
    [property: DataMember, MemoryPackOrder(1)] string? ExternalUrl = null,
    [property: DataMember, MemoryPackOrder(2)] string? AvatarKey = null
) {
    // This record relies on referential equality
    public bool Equals(Picture? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

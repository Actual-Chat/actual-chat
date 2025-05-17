using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record MediaContent(
    [property: DataMember, MemoryPackOrder(0)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(1)] string ContentId,
    [property: DataMember, MemoryPackOrder(2)] MediaId? ThumbnailMediaId = null,
    [property: DataMember, MemoryPackOrder(3)] string? ThumbnailContentId = null
) {
    // This record relies on referential equality
    public bool Equals(MediaContent? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

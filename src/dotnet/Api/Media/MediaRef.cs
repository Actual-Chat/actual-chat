using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

/// <summary>
/// References uploaded media and optional thumbnail.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record MediaRef(
    [property: DataMember, MemoryPackOrder(0), Key(0)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string BlobId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] MediaId? ThumbnailMediaId = null,
    [property: DataMember, MemoryPackOrder(3), Key(3)] string? ThumbnailBlobId = null
) {
    // This record relies on referential equality
    public bool Equals(MediaRef? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

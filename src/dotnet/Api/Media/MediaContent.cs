using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

/// <summary>
/// References uploaded media and optional thumbnail.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record MediaContent(
    [property: DataMember, MemoryPackOrder(0)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(1)] string BlobId,
    [property: DataMember, MemoryPackOrder(2)] MediaId? ThumbnailMediaId = null,
    [property: DataMember, MemoryPackOrder(3)] string? ThumbnailBlobId = null
) {
    // This record relies on referential equality
    public bool Equals(MediaContent? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

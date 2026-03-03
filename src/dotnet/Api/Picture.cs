using ActualLab.Fusion.Blazor;

namespace ActualChat;

/// <summary>
/// Represents an image from media content, external URL, or avatar key.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Picture(
    [property: DataMember, MemoryPackOrder(0)] Media.MediaContent? MediaContent,
    [property: DataMember, MemoryPackOrder(1)] string? ExternalUrl = null,
    [property: DataMember, MemoryPackOrder(2)] string? AvatarKey = null
) {
    // This record relies on referential equality
    public bool Equals(Picture? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

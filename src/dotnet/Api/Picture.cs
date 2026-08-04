using ActualLab.Fusion.Blazor;

namespace ActualChat;

/// <summary>
/// Represents an image from media content, external URL, or avatar key.
/// </summary>
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Picture(
    [property: DataMember, Key(0)] MediaRef? MediaRef,
    [property: DataMember, Key(1)] string? ExternalUrl = null,
    [property: DataMember, Key(2)] string? AvatarKey = null
) {
    // This record relies on referential equality
    public bool Equals(Picture? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

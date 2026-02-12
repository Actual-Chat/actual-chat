using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Media;

#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record MediaFull : Media
{
    [DataMember, MemoryPackOrder(3)] public UserId? UserId { get; init; }
    [DataMember, MemoryPackOrder(4)] public MediaId? ThumbnailId { get; init; }

    public MediaFull(MediaId id) : base(id) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
    public MediaFull(MediaId id, long version, string contentId, UserId? userId, PropertyBag metadata) : base(id, version, contentId, metadata)
        => UserId = userId;

    // This record relies on referential equality
    public bool Equals(MediaFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

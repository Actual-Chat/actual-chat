using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record MediaFull : Media
{
    [DataMember, MemoryPackOrder(3), Key(10)] public UserId? UserId { get; init; }
    [DataMember, MemoryPackOrder(4), Key(11)] public MediaId? ThumbnailId { get; init; }

    public MediaFull(MediaId id) : base(id) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
    public MediaFull(MediaId id, string blobId, long version, MediaKind kind, MetadataBag metadata, UserId? userId, MediaId? thumbnailId)
        : base(id, blobId, version, kind, metadata)
    {
        UserId = userId;
        ThumbnailId = thumbnailId;
    }

    // This record relies on referential equality
    public bool Equals(MediaFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record MediaFull : Media
{
    [DataMember, Key(10)] public UserId? UserId { get; init; }
    [DataMember, Key(11)] public MediaId? ThumbnailId { get; init; }

    public MediaFull(MediaId id) : base(id) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
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

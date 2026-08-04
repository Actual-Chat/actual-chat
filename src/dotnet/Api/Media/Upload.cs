using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

/// <summary>
/// Represents a file upload session with metadata.
/// </summary>
#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record Upload : IHasId<UploadId>, IHasMetadata, IRequirementTarget
{
    [DataMember, Key(0)] public UploadId Id { get; init; }
    [DataMember, Key(1)] public UserId UserId { get; init; }
    [DataMember, Key(5)] public long? Length { get; init; }
    [DataMember, Key(2)] public string Tag { get; init; } = "";
    [DataMember, Key(3)] public string SessionUri { get; init; } = "";
    [DataMember, Key(4)] public MetadataBag Metadata { get; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string FileName {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string ContentType {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    public Upload(UploadId id, UserId userId, long? length, string tag, MetadataBag metadata)
    {
        Id = id;
        UserId = userId;
        Length = length;
        Tag = tag;
        Metadata = metadata;
    }

    private Upload() : this(default!, default!, null, "", default) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public Upload(UploadId id, UserId userId, string tag, string sessionUri, MetadataBag metadata, long? length)
        : this(id, userId, length, tag, metadata)
        => SessionUri = sessionUri;

    // This record relies on referential equality
    public bool Equals(Upload? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

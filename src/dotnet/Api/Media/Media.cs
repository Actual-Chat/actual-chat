using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Media;

/// <summary>
/// Represents uploaded media content (images, audio, video, files).
/// </summary>
#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public partial record Media : IHasId<MediaId>, IHasVersion<long>, IHasMetadata, IRequirementTarget
{
    [DataMember, Key(0)] public MediaId Id { get; init; }
    [DataMember, Key(1)] public string BlobId { get; init; } = "";
    [DataMember, Key(2)] public long Version { get; init; }
    [DataMember, Key(3)] public MediaKind Kind { get; init; }
    [DataMember, Key(4)] public MetadataBag Metadata { get; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public bool IsReady => !BlobId.IsNullOrEmpty();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long Length {
        get => this.GetMetadataValue(0L);
        init => this.SetMetadataValue(value);
    }

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

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int Width {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int Height {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long DurationMs {
        get => this.GetMetadataValue(0L);
        init => this.SetMetadataValue(value);
    }

    // Used when Kind = ChatEntryXxx

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Moment BeginsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Moment EndsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Moment ContentEndsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Moment ClientSideBeginsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public bool IsUploaded => !BlobId.IsNullOrEmpty();

    public Media(MediaId id)
        => Id = id;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public Media(MediaId id, string blobId, long version, MediaKind kind, MetadataBag metadata)
    {
        Id = id;
        BlobId = blobId;
        Version = version;
        Kind = kind;
        Metadata = metadata;
    }

    // This record relies on referential equality
    public virtual bool Equals(Media? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

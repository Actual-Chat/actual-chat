using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Media;

/// <summary>
/// Represents uploaded media content (images, audio, video, files).
/// </summary>
#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record Media : IHasId<MediaId>, IHasVersion<long>, IHasMetadata, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0), Key(0)] public MediaId Id { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public string BlobId { get; init; } = "";
    [DataMember, MemoryPackOrder(2), Key(2)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(9), Key(3)] public MediaKind Kind { get; init; }
    [DataMember, MemoryPackOrder(10), Key(4)] public PropertyBag Metadata { get; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsReady => !BlobId.IsNullOrEmpty();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long Length {
        get => this.GetMetadataValue(0L);
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string FileName {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string ContentType {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int Width {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int Height {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long DurationMs {
        get => this.GetMetadataValue(0L);
        init => this.SetMetadataValue(value);
    }

    // Used when Kind = ChatEntryXxx

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Moment BeginsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Moment EndsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Moment ContentEndsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Moment ClientSideBeginsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsUploaded => !BlobId.IsNullOrEmpty();

    public Media(MediaId id)
        => Id = id;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
    public Media(MediaId id, string blobId, long version, MediaKind kind, PropertyBag metadata)
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

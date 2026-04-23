using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Media;

/// <summary>
/// Represents uploaded media content (images, audio, video, files).
/// </summary>
#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
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

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore=true)]
    public long Length {
        get => this.GetMetadataValue(0L);
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore=true)]
    public string FileName {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore=true)]
    public string ContentType {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore=true)]
    public int Width {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore=true)]
    public int Height {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    // Used when Kind = ChatEntryXxx

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore=true)]
    public Moment BeginsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore=true)]
    public Moment EndsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore=true)]
    public Moment ContentEndsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore=true)]
    public Moment ClientSideBeginsAt {
        get => this.GetMetadataValue<Moment>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsUploaded => !BlobId.IsNullOrEmpty();

    public Media(MediaId id)
        => Id = id;

    [ConstructorShape, JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public Media(MediaId id, long version, string blobId, PropertyBag metadata)
    {
        Id = id;
        Version = version;
        BlobId = blobId;
        Metadata = metadata;
    }

    // This record relies on referential equality
    public virtual bool Equals(Media? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

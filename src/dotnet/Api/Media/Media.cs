using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Media;

/// <summary>
/// Represents uploaded media content (images, audio, video, files).
/// </summary>
#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record Media : IHasId<MediaId>, IHasVersion<long>, IHasMetadata, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public MediaId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string BlobId { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(10)] public PropertyBag Metadata { get; init; }

    // Computed properties

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

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsReady => !BlobId.IsNullOrEmpty();

    public Media(MediaId id)
        => Id = id;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
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

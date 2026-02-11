using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Media : IHasId<MediaId>, IHasMetadata, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public MediaId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string ContentId { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(10)] public PropertyBag Metadata { get; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long Length {
        get => this.GetMetadataValue(0L);
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string FileName {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string ContentType {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public int Width {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public int Height {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsReady => !ContentId.IsNullOrEmpty();

    public Media(MediaId id)
        => Id = id;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public Media(MediaId id, long version, string contentId, PropertyBag metadata)
    {
        Id = id;
        Version = version;
        ContentId = contentId;
        Metadata = metadata;
    }

    // This record relies on referential equality
    public virtual bool Equals(Media? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

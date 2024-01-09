using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Media : IHasId<MediaId>, IHasMetadata, IRequirementTarget
{
    private readonly NewtonsoftJsonSerialized<ImmutableOptionSet> _metadata =
        NewtonsoftJsonSerialized.New(ImmutableOptionSet.Empty);

    [DataMember, MemoryPackOrder(0)] public MediaId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string ContentId { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public string MetadataJson {
#pragma warning disable IL2026
        get => _metadata.Data;
#pragma warning restore IL2026
        init => _metadata.Data = value;
    }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ImmutableOptionSet Metadata {
#pragma warning disable IL2026
        get => _metadata.Value;
#pragma warning restore IL2026
        set => _metadata.Value = value;
    }

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

    public Media() : this(MediaId.None) { }
    public Media(MediaId id)
        => Id = id;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public Media(MediaId id, string contentId, string metadataJson)
    {
        Id = id;
        ContentId = contentId;
        MetadataJson = metadataJson;
    }

    // This record relies on referential equality
    public bool Equals(Media? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

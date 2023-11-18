using MemoryPack;
using Stl.Fusion.Blazor;

namespace ActualChat.Media;

#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Media : IHasId<MediaId>, IRequirementTarget
{
    private readonly NewtonsoftJsonSerialized<ImmutableOptionSet> _metadata =
        NewtonsoftJsonSerialized.New(ImmutableOptionSet.Empty);

    [DataMember, MemoryPackOrder(0)] public MediaId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string ContentId { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public string MetadataJson {
        get => _metadata.Data;
        init => _metadata.Data = value;
    }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ImmutableOptionSet Metadata {
        get => _metadata.Value;
        init => _metadata.Value = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long Length {
        get => GetMetadataValue(0L);
        init => SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string FileName {
        get => GetMetadataValue("");
        init => SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string ContentType {
        get => GetMetadataValue("");
        init => SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public int Width {
        get => GetMetadataValue<int>();
        init => SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public int Height {
        get => GetMetadataValue<int>();
        init => SetMetadataValue(value);
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

    // Private methods

    private T GetMetadataValue<T>(T @default = default!, [CallerMemberName] string symbol = "")
    {
        var value = Metadata[symbol];
        if (value == null)
            return @default;

        // TODO: remove this workaround when int is not deserialized as long
        if (typeof(T) == typeof(int))
            value = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        return (T)value;
    }

    private void SetMetadataValue<T>(T value, [CallerMemberName] string symbol = "")
        => _metadata.Value = Metadata.Set(symbol, value);
}

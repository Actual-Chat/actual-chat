using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public sealed record TextEntryAttachment(
    [property: DataMember] Symbol Id,
    [property: DataMember] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    private readonly NewtonsoftJsonSerialized<ImmutableOptionSet> _metadata =
        NewtonsoftJsonSerialized.New(ImmutableOptionSet.Empty);

    [DataMember] public TextEntryId EntryId { get; init; }
    [DataMember] public int Index { get; init; }
    [DataMember] public string ContentId { get; init; } = "";

    [DataMember] public string MetadataJson {
        get => _metadata.Data;
        init => _metadata.Data = value;
    }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => EntryId.ChatId;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ImmutableOptionSet Metadata {
        get => _metadata.Value;
        init => _metadata.Value = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long Length {
        get => GetMetadataValue(0L);
        init => SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string FileName {
        get => GetMetadataValue("");
        init => SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Description {
        get => GetMetadataValue("");
        init => SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string ContentType {
        get => GetMetadataValue("");
        init => SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public int Width {
        get => GetMetadataValue<int>();
        init => SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public int Height {
        get => GetMetadataValue<int>();
        init => SetMetadataValue(value);
    }

    public TextEntryAttachment() : this(Symbol.Empty) { }

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

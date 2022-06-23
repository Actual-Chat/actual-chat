namespace ActualChat.Chat;

public record TextEntryAttachment
{
    private readonly NewtonsoftJsonSerialized<ImmutableOptionSet> _metadata =
        NewtonsoftJsonSerialized.New(ImmutableOptionSet.Empty);

    public Symbol ChatId { get; init; }
    public long EntryId { get; init; }
    public int Index { get; init; }
    public long Version { get; init; }
    public string ContentId { get; init; } = "";

    public string MetadataJson {
        get => _metadata.Data;
        set => _metadata.Data = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ImmutableOptionSet Metadata {
        get => _metadata.Value;
        set => _metadata.Value = value;
    }

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
        => Metadata = Metadata.Set(symbol, value);

    public long Length {
        get => GetMetadataValue(0L);
        init => SetMetadataValue(value);
    }

    public string FileName {
        get => GetMetadataValue("");
        init => SetMetadataValue(value);
    }

    public string Description {
        get => GetMetadataValue("");
        init => SetMetadataValue(value);
    }

    public string ContentType {
        get => GetMetadataValue("");
        init => SetMetadataValue(value);
    }

    public int Width {
        get => GetMetadataValue<int>();
        init => SetMetadataValue(value);
    }

    public int Height {
        get => GetMetadataValue<int>();
        init => SetMetadataValue(value);
    }
}

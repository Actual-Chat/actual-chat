using Newtonsoft.Json;

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
    public string Url => "/api/content/" + ContentId;
    public string ProxyUrl => Url;

    public string MetadataJson {
        get => _metadata.Data;
        set => _metadata.Data = value;
    }

    [JsonIgnore]
    public ImmutableOptionSet Metadata {
        get => _metadata.Value;
        set => _metadata.Value = value;
    }

    private T GetMetadataValue<T>(Symbol symbol, T defaultValue)
    {
        var obj = Metadata[symbol];
        return obj != null ? (T)obj : defaultValue;
    }

    private void SetMetadataValue<T>(Symbol symbol, T value)
        => Metadata = Metadata.Set(symbol, value);

    public long Length {
        get => GetMetadataValue(nameof(Length), 0L);
        init => SetMetadataValue(nameof(Length), value);
    }

    public string FileName {
        get => GetMetadataValue<string>(nameof(FileName), "");
        init => SetMetadataValue(nameof(FileName), value);
    }

    public string Description {
        get => GetMetadataValue<string>(nameof(Description), "");
        init => SetMetadataValue(nameof(Description), value);
    }

    public string ContentType {
        get => GetMetadataValue<string>(nameof(ContentType), "");
        init => SetMetadataValue(nameof(ContentType), value);
    }

    public int Width {
        get => GetMetadataValue(nameof(Width), 0);
        init => SetMetadataValue(nameof(Width), value);
    }

    public int Height {
        get => GetMetadataValue(nameof(Height), 0);
        init => SetMetadataValue(nameof(Height), value);
    }
}

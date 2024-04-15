using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.Search;

internal sealed class OpenSearchJsonSerializer(IOpenSearchSerializer builtin, IConnectionSettingsValues settings)
    : IOpenSearchSerializer
{

    private readonly JsonSerializerOptions _serializerOptions = new() {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly JsonSerializerOptions _indentedSerializerOptions = new() {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private IOpenSearchSerializer Builtin { get; } = builtin;
    private IConnectionSettingsValues Settings { get; } = settings;

    // TODO(AK): reuse same code
    // TODO: Delegate OpenSearch'es internal type serialization to built-in serializer
    // TODO: Make sure connection settings are respected

    public object Deserialize(Type type, Stream stream) => JsonSerializer.Deserialize(stream, type, _serializerOptions)!;

    public T Deserialize<T>(Stream stream) => JsonSerializer.Deserialize<T>(stream, _serializerOptions)!;

    public async Task<object> DeserializeAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
        => (await JsonSerializer.DeserializeAsync(stream, type, _serializerOptions, cancellationToken).ConfigureAwait(false))!;

    public async Task<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        => (await JsonSerializer.DeserializeAsync<T>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false))!;

    public void Serialize<T>(T data, Stream stream, SerializationFormatting formatting = SerializationFormatting.None)
    {
        var options = formatting == SerializationFormatting.Indented
            ? _indentedSerializerOptions
            : _serializerOptions;
        JsonSerializer.Serialize(stream, data, options);
    }

    public async Task SerializeAsync<T>(T data, Stream stream, SerializationFormatting formatting = SerializationFormatting.None, CancellationToken cancellationToken = default)
    {
        var options = formatting == SerializationFormatting.Indented
            ? _indentedSerializerOptions
            : _serializerOptions;
        await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken).ConfigureAwait(false);
    }
}

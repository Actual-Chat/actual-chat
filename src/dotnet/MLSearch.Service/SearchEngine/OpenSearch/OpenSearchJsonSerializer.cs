using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch;
internal class OpenSearchJsonSerializer : IOpenSearchSerializer
{
    private IOpenSearchSerializer _builtin;
    private IConnectionSettingsValues _settings;
    private readonly JsonSerializerOptions serializerOptions;
    private readonly JsonSerializerOptions indentedSerializerOptions;


    public OpenSearchJsonSerializer(IOpenSearchSerializer builtin, IConnectionSettingsValues settings)
    {
        // TODO: Delegate OpenSearch'es internal type serialization to built-in serializer
        // TODO: Make sure connection settings are respected
        _builtin = builtin;
        _settings = settings;
        serializerOptions = new() {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        indentedSerializerOptions = new() {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
    }

    public object Deserialize(Type type, Stream stream) => JsonSerializer.Deserialize(stream, type, serializerOptions)!;

    public T Deserialize<T>(Stream stream) => JsonSerializer.Deserialize<T>(stream, serializerOptions)!;

    public async Task<object> DeserializeAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
        => (await JsonSerializer.DeserializeAsync(stream, type, serializerOptions, cancellationToken).ConfigureAwait(false))!;

    public async Task<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        => (await JsonSerializer.DeserializeAsync<T>(stream, serializerOptions, cancellationToken).ConfigureAwait(false))!;

    public void Serialize<T>(T data, Stream stream, SerializationFormatting formatting = SerializationFormatting.None)
    {
        var options = formatting == SerializationFormatting.Indented ? indentedSerializerOptions : serializerOptions;
        JsonSerializer.Serialize(stream, data, options);
    }

    public async Task SerializeAsync<T>(T data, Stream stream, SerializationFormatting formatting = SerializationFormatting.None, CancellationToken cancellationToken = default)
    {
        var options = formatting == SerializationFormatting.Indented ? indentedSerializerOptions : serializerOptions;
        await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken).ConfigureAwait(false);
    }
}
